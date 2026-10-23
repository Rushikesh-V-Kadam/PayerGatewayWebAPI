using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Constants;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Models.Fhir;
using PayerGatewayWebAPI.Models.Requests;
using PayerGatewayWebAPI.Models.Responses;
using PayerGatewayWebAPI.Repositories.Interfaces;
using PayerGatewayWebAPI.Services.Payers;

namespace PayerGatewayWebAPI.Services.Pas;

/// <summary>
/// STEP 3 - PAS (Prior Authorization Support). Sends prior authorization requests to the payer named by the EHR
/// and tracks the decision.
///
///   submit  : EHR request -> FHIR data from the FHIR API (+ completed DTR forms, documents) -> PAS Bundle
///             -> POST {payer}/Claim/$submit -> ClaimResponse -> PriorAuthDecision -> saved, returned
///   update  : same, with the Claim Update profile; removed orders are sent as cancelled lines
///   cancel  : every line sent as cancelled
///   inquire : POST {payer}/Claim/$inquire to get the latest decision of a pended request
///   notification : the payer says something changed -> inquire the payer's open requests
/// A pended request can also get a payer information request (Task) -> STEP 4, CdexService.
///
/// The payer can refuse a request in two ways, and both are handled the same (422, nothing saved, the EHR corrects
/// the data and sends the same requestId again): an HTTP 4xx with an OperationOutcome, or an HTTP 200 with a
/// ClaimResponse that says outcome "error" and has no line decision (ClaimResponse.error, e.g. "member not found").
///
/// Once the payer has accepted a request, the result is saved even when the EHR has stopped waiting for the answer
/// (the save does not use the caller's cancellation), so an accepted request is never lost.
///
/// Every decision is saved in table PA_PRIOR_AUTH in the EHR database (through the EHR data API). The EHR reads it
/// from that table, so the gateway does not send a second copy ("write-back") to the EHR.
/// </summary>
public class PasService
{
    private readonly FhirApiClient _fhir;
    private readonly PayerRegistry _payers;
    private readonly PayerClient _payerClient;
    private readonly PasBundleBuilder _builder;
    private readonly PasSubscriptionService _subscriptions;
    private readonly FacilityResolver _facilities;
    private readonly IPriorAuthRepository _repository;
    private readonly WorkflowSettings _workflow;
    private readonly ILogger<PasService> _logger;

    /// <summary>Created by dependency injection.</summary>
    public PasService(FhirApiClient fhir, PayerRegistry payers, PayerClient payerClient, PasBundleBuilder builder,
        PasSubscriptionService subscriptions, FacilityResolver facilities, IPriorAuthRepository repository, IOptions<WorkflowSettings> workflow, ILogger<PasService> logger)
    {
        _fhir = fhir;
        _payers = payers;
        _payerClient = payerClient;
        _builder = builder;
        _subscriptions = subscriptions;
        _facilities = facilities;
        _repository = repository;
        _workflow = workflow.Value;
        _logger = logger;
    }

    // ================================================================== submit / update

    /// <summary>
    /// Submits a new request (isUpdate = false) or changes an existing one (isUpdate = true).
    /// Nothing is saved unless the payer accepts the request.
    /// </summary>
    public async Task<PriorAuthDecision> SubmitAsync(PriorAuthRequest request, bool isUpdate, CancellationToken ct)
    {
        // ---- 1. check the input
        CheckRequest(request);
        PayerSettings payer = await _payers.GetRequiredAsync(request.PayerName, 400, ct);
        List<string> orderIds = TextHelper.Distinct(request.OrderIds);

        // A request id can be submitted again only when the earlier request is cancelled, or has status "error"
        // (a row from an earlier gateway version, which saved requests the payer had refused; they are not saved now).
        PriorAuthRecord? existing = await _repository.GetAsync(request.RequestId, ct);
        if (!isUpdate && existing != null && existing.Status != DecisionStatus.Cancelled && existing.Status != DecisionStatus.Error)
        {
            throw new ApiException(409, "Request " + request.RequestId + " was already submitted. Use PUT /api/v1/pas/requests/" + request.RequestId + " to change it.");
        }
        if (isUpdate && existing == null)
        {
            throw new ApiException(404, "Request " + request.RequestId + " was not found.");
        }
        if (isUpdate && existing != null)
        {
            if (existing.PatientId != request.PatientId)
            {
                throw new ApiException(400, "patientId cannot change on an update.");
            }
            if (!string.Equals(existing.PayerName, payer.PayerName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiException(400, "The payer of an existing request cannot change. Cancel it and submit a new request.");
            }
            if (existing.Status == DecisionStatus.Cancelled)
            {
                throw new ApiException(409, "Request " + request.RequestId + " is cancelled. Submit it again with POST /api/v1/pas/requests.");
            }
        }

        // ---- 2. data from the FHIR API
        JsonObject? patient = await _fhir.ReadAsync("Patient", request.PatientId, ct);
        if (patient == null)
        {
            throw new ApiException(404, "Patient " + request.PatientId + " was not found at the FHIR API.");
        }

        List<JsonObject> orders = await _fhir.ReadOrdersAsync(orderIds, ct);
        OrderHelper.CheckOrders(orderIds, orders, request.PatientId);
        CheckOrdersHaveCode(orders);

        // CLIENT-EHR: the insurance is the one the EHR sends (coverageId), read by its id at the FHIR API; the orders
        // carry no insurance. An update without coverageId keeps the insurance of the request.
        string? requestedCoverageId = request.CoverageId;
        if (string.IsNullOrWhiteSpace(requestedCoverageId) && existing != null)
        {
            requestedCoverageId = existing.CoverageId;
        }
        if (string.IsNullOrWhiteSpace(requestedCoverageId))
        {
            throw new ApiException(400, "The request is not valid.", new List<string> { "coverageId is required." });
        }
        string coverageId = requestedCoverageId.Trim();
        Coverage? coverage = await _fhir.ReadCoverageAsync(coverageId, ct);
        string coverageProblem = OrderHelper.CoverageProblem(coverage, coverageId, request.PatientId);
        if (coverage == null || coverageProblem.Length > 0)
        {
            throw new ApiException(422, coverageProblem);
        }
        // CLIENT-EHR: the member id may be missing in the client's EHR.
        // The member id (Coverage.subscriberId) is optional in FHIR R4 (0..1): the request is sent without it when the
        // insurance has none. A real payer will most likely need it to find the member (good to have).
        if (string.IsNullOrWhiteSpace(coverage.MemberId()))
        {
            _logger.LogWarning("Insurance {CoverageId} of patient {PatientId} has no member id: request {RequestId} is sent without it",
                coverage.Id, request.PatientId, request.RequestId);
        }

        // CLIENT-EHR: the client's organization has no NPI, each facility (location) has one, so Claim.provider is the
        // facility, not the parent organization.
        // Claim.provider (1..1 in FHIR) is the facility of orderLocationCode, with its own NPI (see FacilityResolver).
        // CLIENT-EHR: the place of service also comes from orderLocationCode (the location), not from the orders.
        PasFacility facility = await _facilities.ResolveAsync(request, ct);
        string organizationId = facility.OrganizationId;
        JsonObject? organization = await _fhir.ReadAsync("Organization", organizationId, ct);
        if (organization == null)
        {
            throw new ApiException(404, "Organization " + organizationId + " was not found at the FHIR API.");
        }

        // The NPI is optional in the PAS Requestor profile (identifier:NPI 0..*, must support): it is sent when the
        // organization has one. Without it the payer cannot be asked to notify us (PAS Subscription) and cannot be
        // searched for information requests (CDex Task?owner=NPI); POST .../inquire (called by the client EHR) still finds the decision.
        string? organizationNpi = ReadNpi(organization);
        if (organizationNpi == null)
        {
            _logger.LogWarning("Organization {OrganizationId} has no NPI: request {RequestId} is sent without it; PAS Subscription and CDex Task search are skipped",
                organizationId, request.RequestId);
        }

        // The requesting clinician: always the one in the request (required, checked in CheckRequest).
        string practitionerId = request.RequestingPractitionerId.Trim();
        JsonObject? practitioner = await _fhir.ReadAsync("Practitioner", practitionerId, ct);
        if (practitioner == null)
        {
            throw new ApiException(404, "Requesting practitioner " + practitionerId + " was not found at the FHIR API.");
        }
        // the practitioner NPI is optional (Practitioner.identifier 0..*): sent when the practitioner has one
        string? practitionerNpi = ReadNpi(practitioner);

        JsonObject? referring = null;
        if (!string.IsNullOrWhiteSpace(request.ReferringPractitionerId))
        {
            referring = await _fhir.ReadAsync("Practitioner", request.ReferringPractitionerId, ct);
            if (referring == null)
            {
                throw new ApiException(404, "Referring practitioner " + request.ReferringPractitionerId + " was not found at the FHIR API.");
            }
        }

        List<JsonObject> forms = await ReadFormsAsync(request, orderIds, ct);
        List<JsonObject> documents = await ReadDocumentsAsync(request.DocumentIds, ct);

        // ---- 3. orders removed on update become cancelled lines; every order keeps its line number
        List<string> previousOrderIds = existing != null ? existing.OrderIds : new List<string>();
        List<string> removedIds = new List<string>();
        if (isUpdate)
        {
            foreach (string previousId in previousOrderIds)
            {
                if (!orderIds.Contains(previousId))
                {
                    removedIds.Add(previousId);
                }
            }
        }
        List<JsonObject> removedOrders = new List<JsonObject>();
        if (removedIds.Count > 0)
        {
            removedOrders = await _fhir.ReadOrdersAsync(removedIds, ct);
            CheckOrdersHaveCode(removedOrders);
        }

        List<string> allLineOrderIds = new List<string>(orderIds);
        allLineOrderIds.AddRange(removedIds);
        Dictionary<string, int> startNumbers = isUpdate && existing != null ? existing.LineNumbers : new Dictionary<string, int>();
        Dictionary<string, int> lineNumbers = AssignLineNumbers(startNumbers, allLineOrderIds);

        // ---- 4. build and send the PAS Bundle
        int version = (isUpdate && existing != null ? existing.Version : 0) + 1;
        PasBuildInput input = new PasBuildInput();
        input.RequestId = request.RequestId;
        input.Version = version;
        input.Priority = request.Priority;
        input.Payer = payer;
        input.Patient = patient;
        input.Coverage = coverage;
        input.Organization = organization;
        input.PlaceOfServiceCode = facility.PlaceOfServiceCode;
        input.Practitioner = practitioner;
        input.Orders = orders;
        input.CancelledOrders = removedOrders;
        input.PreviousOrderIds = previousOrderIds;
        input.LineNumbers = lineNumbers;
        input.Forms = forms;
        input.Documents = documents;
        input.Notes = request.Notes;
        input.RequestType = request.RequestType;
        input.PreviousAuthorizationNumber = request.PreviousAuthorizationNumber;
        input.PreviousServiceStart = request.PreviousServiceStart;
        input.PreviousServiceEnd = request.PreviousServiceEnd;
        input.PreviousTreatments = request.PreviousTreatments;
        input.PatientType = request.PatientType;
        input.ReferringPractitioner = referring;
        Bundle bundle = _builder.BuildRequest(input);

        PayerCallResult result = await _payerClient.PostFhirAsync(payer, PasCodes.Submit, bundle, ct);
        ClaimResponse? claimResponse = PasDecisionReader.FindClaimResponse(result);
        if (!result.IsSuccess() || claimResponse == null)
        {
            // The payer did not accept the request (e.g. validation error). Nothing is saved; the EHR can fix and retry.
            PriorAuthDecision rejected = PasDecisionReader.Rejected(request.RequestId, payer.PayerName, request.PatientId, orderIds, result);
            int status = result.StatusCode >= 400 && result.StatusCode < 500 ? 422 : 502;
            throw new ApiException(status, string.Join("; ", rejected.Errors), rejected);
        }
        ThrowIfRefused(claimResponse, request.RequestId, payer.PayerName, request.PatientId, orderIds);

        // ---- 5. accepted: save the new state
        PriorAuthRecord record;
        if (isUpdate && existing != null)
        {
            record = existing;
        }
        else
        {
            record = new PriorAuthRecord();
            record.RequestId = request.RequestId;
            record.SubmittedAt = DateTime.UtcNow;
        }
        record.PayerName = payer.PayerName;
        record.PatientId = request.PatientId;
        record.CoverageId = coverageId;
        record.MemberId = coverage.MemberId();
        record.OrganizationId = organizationId;
        record.OrganizationNpi = organizationNpi;
        record.PractitionerId = practitionerId;
        record.PractitionerNpi = practitionerNpi;
        record.Priority = request.Priority;
        record.Version = version;
        record.OrderIds = orderIds;
        record.LineNumbers = lineNumbers;
        await ApplyResponseAsync(record, bundle, result, claimResponse, false);

        // never fails the request: a subscription problem is only logged (see PasSubscriptionService).
        // The payer filters notifications by organization NPI, so there is no subscription without one.
        if (organizationNpi != null)
        {
            await _subscriptions.EnsureSubscribedAsync(payer, organizationNpi, ct);
        }
        return record.Decision;
    }

    // ================================================================== cancel

    /// <summary>Cancels the whole request: every line is sent again as cancelled (X12 certification type 3).</summary>
    public async Task<PriorAuthDecision> CancelAsync(string requestId, CancelPriorAuthRequest cancel, CancellationToken ct)
    {
        PriorAuthRecord record = await GetRecordAsync(requestId, ct);
        if (record.Status == DecisionStatus.Cancelled)
        {
            return record.Decision;
        }
        PayerSettings payer = await GetPayerAsync(record, ct);

        JsonObject? patient = await _fhir.ReadAsync("Patient", record.PatientId, ct);
        // CLIENT-EHR: the insurance of the request is read by its id (the EHR's coverageId); it must still be active.
        Coverage? coverage = await _fhir.ReadCoverageAsync(record.CoverageId, ct);
        string coverageProblem = OrderHelper.CoverageProblem(coverage, record.CoverageId, record.PatientId);
        if (coverageProblem.Length > 0)
        {
            throw new ApiException(422, coverageProblem);
        }
        JsonObject? organization = await _fhir.ReadAsync("Organization", record.OrganizationId, ct);
        List<JsonObject> orders = await _fhir.ReadOrdersAsync(record.OrderIds, ct);
        if (patient == null || coverage == null || organization == null || orders.Count == 0)
        {
            throw new ApiException(422, "The patient, insurance, organization or orders of this request can no longer be read.");
        }
        CheckOrdersHaveCode(orders);

        PasBuildInput input = new PasBuildInput();
        input.RequestId = record.RequestId;
        input.Version = record.Version + 1;
        input.Priority = record.Priority;
        input.Payer = payer;
        input.Patient = patient;
        input.Coverage = coverage;
        input.Organization = organization;
        input.CancelledOrders = orders;
        input.PreviousOrderIds = record.OrderIds;
        input.LineNumbers = record.LineNumbers;
        input.Notes = cancel.Reason;
        Bundle bundle = _builder.BuildRequest(input);

        PayerCallResult result = await _payerClient.PostFhirAsync(payer, PasCodes.Submit, bundle, ct);
        ClaimResponse? claimResponse = PasDecisionReader.FindClaimResponse(result);
        if (!result.IsSuccess() || claimResponse == null)
        {
            PriorAuthDecision rejected = PasDecisionReader.Rejected(record.RequestId, payer.PayerName, record.PatientId, record.OrderIds, result);
            int status = result.StatusCode >= 400 && result.StatusCode < 500 ? 422 : 502;
            throw new ApiException(status, string.Join("; ", rejected.Errors), rejected);
        }
        // the payer refused the cancellation: the request stays as it was (nothing is saved)
        ThrowIfRefused(claimResponse, record.RequestId, payer.PayerName, record.PatientId, record.OrderIds);

        record.Version = record.Version + 1;
        await ApplyResponseAsync(record, bundle, result, claimResponse, true);
        return record.Decision;
    }

    // ================================================================== read / inquire

    /// <summary>Latest stored decision (no payer call).</summary>
    public async Task<PriorAuthDecision> GetAsync(string requestId, CancellationToken ct)
    {
        PriorAuthRecord record = await GetRecordAsync(requestId, ct);
        return record.Decision;
    }

    /// <summary>
    /// Asks the payer for the latest decision (Claim/$inquire). The decision is saved only when it is different
    /// from the stored one. When the payer cannot answer the inquiry (HTTP error, or a ClaimResponse that only
    /// carries errors), the stored decision stays as it is and the caller gets 502: a failed question is not a decision.
    /// </summary>
    public async Task<PriorAuthDecision> InquireAsync(string requestId, CancellationToken ct)
    {
        PriorAuthRecord record = await GetRecordAsync(requestId, ct);
        if (record.LastRequestBundleJson == null)
        {
            return record.Decision;
        }
        if (record.Status == DecisionStatus.Cancelled)
        {
            // A cancelled request is finished: the payer is not asked again and nothing can reopen it.
            // (The same request id can be submitted again with POST /api/v1/pas/requests.)
            return record.Decision;
        }
        PayerSettings payer = await GetPayerAsync(record, ct);

        Bundle inquiry = _builder.BuildInquiry(record.LastRequestBundleJson);
        PayerCallResult result = await _payerClient.PostFhirAsync(payer, PasCodes.Inquire, inquiry, ct);
        if (!result.IsSuccess())
        {
            throw new ApiException(502, "Inquiry failed at " + payer.GetDisplayName() + " (HTTP " + result.StatusCode + ").", result.Messages());
        }
        ClaimResponse? claimResponse = PasDecisionReader.FindClaimResponse(result);
        if (claimResponse == null)
        {
            return record.Decision;   // the payer has nothing newer
        }
        if (PasDecisionReader.IsRefusal(claimResponse))
        {
            // The payer could not answer the question (e.g. it does not find the request). That says nothing about
            // the request itself, so the stored decision is not replaced by "error" and the request stays open.
            throw new ApiException(502, "Inquiry failed at " + payer.GetDisplayName() + ": the payer returned errors instead of a decision.",
                PasDecisionReader.ErrorMessages(claimResponse));
        }
        if (claimResponse.Item.Count == 0)
        {
            // The answer has no line decision at all, so it says nothing new about the lines. The stored decision
            // (which may already be approved or denied) is kept; it is never replaced by an empty "pended" one.
            return record.Decision;
        }

        PriorAuthDecision before = record.Decision;
        Claim claim = ReadClaim(record.LastRequestBundleJson);
        PriorAuthDecision latest = PasDecisionReader.Read(claimResponse, claim, record, _workflow);

        // The same overall result as before: keep the time it was first decided (an inquiry must not move it)
        if (before.DecidedAt != null && before.OverallStatus == latest.OverallStatus)
        {
            latest.DecidedAt = before.DecidedAt;
        }

        // Nothing new from the payer: do not save. A pended request can be inquired many times; saving each time
        // would change UPDATED_AT of PA_PRIOR_AUTH although nothing changed (the EHR may look for changed rows by
        // that column) and would write the large Bundles again for nothing.
        if (ApiJson.Write(before) == ApiJson.Write(latest))
        {
            return record.Decision;
        }

        record.Decision = latest;
        record.Status = latest.OverallStatus;
        record.AuthorizationNumber = latest.AuthorizationNumber;
        if (result.Body != null)
        {
            record.LastResponseBundleJson = result.Body.ToJsonString();
        }
        // saved even when the caller has stopped waiting: the new decision must not be lost
        await _repository.SaveAsync(record, CancellationToken.None);

        _logger.LogInformation("Request {RequestId}: decision is now {Status}", requestId, latest.OverallStatus);
        return record.Decision;
    }

    /// <summary>
    /// A PAS Subscription notification from a payer. The payload is id-only (it does not say which request changed),
    /// so every open request of that payer is inquired. Handshake / heartbeat notifications need no action.
    /// </summary>
    public async Task<NotificationResult> HandleNotificationAsync(string payerName, JsonObject? body, CancellationToken ct)
    {
        PayerSettings payer = await _payers.GetRequiredAsync(payerName, 404, ct);

        NotificationResult outcome = new NotificationResult();
        string type;
        try
        {
            type = NotificationType(body);
        }
        catch (JsonException ex)
        {
            // the sender's mistake, so 400 (a payer must not keep re-sending the same body)
            throw new ApiException(400, "The notification is not a valid FHIR Bundle: " + ex.Message);
        }
        if (type == "handshake" || type == "heartbeat")
        {
            outcome.Status = type;
            return outcome;
        }

        List<PriorAuthRecord> open = await _repository.GetOpenAsync(payer.PayerName, ct);
        foreach (PriorAuthRecord request in open)
        {
            await TryInquireAsync(request.RequestId, ct);
        }
        outcome.Status = open.Count + " open request(s) checked";
        outcome.Count = open.Count;
        return outcome;
    }

    /// <summary>
    /// Runs InquireAsync and returns the decision, or null when the inquiry failed. A failure (the payer, the FHIR API
    /// or the EHR data API did not work, or stored data could not be read) is written to the log and not passed on.
    /// Used where the inquiry is an extra step that must not fail the main work: after a payer notification (the
    /// other open requests must still be checked) and after attachments were sent.
    /// </summary>
    public async Task<PriorAuthDecision?> TryInquireAsync(string requestId, CancellationToken ct)
    {
        try
        {
            return await InquireAsync(requestId, ct);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Inquiry of {RequestId} failed", requestId);
        }
        catch (PayerException ex)
        {
            _logger.LogWarning(ex, "Inquiry of {RequestId} failed: payer", requestId);
        }
        catch (FhirApiException ex)
        {
            _logger.LogWarning(ex, "Inquiry of {RequestId} failed: FHIR API", requestId);
        }
        catch (EhrException ex)
        {
            _logger.LogWarning(ex, "Inquiry of {RequestId} failed: EHR data API", requestId);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Inquiry of {RequestId} failed: data could not be read", requestId);
        }
        return null;
    }

    // ================================================================== helpers

    /// <summary>
    /// Stops with 422 when the payer answered HTTP 200 but did not take the request: the ClaimResponse says "error"
    /// and has no line decision. The payer's error texts are returned to the EHR and nothing is saved, exactly as for
    /// a request the payer rejected with HTTP 4xx.
    /// </summary>
    private static void ThrowIfRefused(ClaimResponse claimResponse, string requestId, string payerName, string patientId, List<string> orderIds)
    {
        if (!PasDecisionReader.IsRefusal(claimResponse))
        {
            return;
        }
        PriorAuthDecision refused = PasDecisionReader.Refused(requestId, payerName, patientId, orderIds, PasDecisionReader.ErrorMessages(claimResponse));
        throw new ApiException(422, string.Join("; ", refused.Errors), refused);
    }

    /// <summary>
    /// Every order needs a code (the service or the medication): it becomes Claim.item.productOrService, which PAS
    /// requires. An order without one cannot be sent; 422 names the order so it can be corrected in the EHR.
    /// </summary>
    private static void CheckOrdersHaveCode(List<JsonObject> orders)
    {
        foreach (JsonObject raw in orders)
        {
            OrderResource order = OrderHelper.ReadOrder(raw);
            CodeableConcept? code = order.GetCode();
            if (code == null || string.IsNullOrWhiteSpace(code.FirstCode()))
            {
                throw new ApiException(422, "Order " + order.Id + " has no code (ServiceRequest.code or MedicationRequest.medicationCodeableConcept). "
                    + "A prior authorization line needs the service or medication code.");
            }
        }
    }

    /// <summary>Required fields and allowed values; all problems are returned together (400).</summary>
    private static void CheckRequest(PriorAuthRequest request)
    {
        // empty header values mean the usual case: a new request for a permanent patient
        if (string.IsNullOrWhiteSpace(request.RequestType))
        {
            request.RequestType = PasCodes.RequestTypeNew;
        }

        if (string.IsNullOrWhiteSpace(request.PatientType))
        {
            request.PatientType = PasCodes.PatientTypePermanent;
        }

        List<string> problems = new List<string>();
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            problems.Add("requestId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.PayerName))
        {
            problems.Add("payerName is required.");
        }

        if (string.IsNullOrWhiteSpace(request.PatientId))
        {
            problems.Add("patientId is required.");
        }

        if (TextHelper.Distinct(request.OrderIds).Count == 0)
        {
            problems.Add("orderIds must contain at least one order id.");
        }

        // CLIENT-EHR: required by the gateway: the order location (facility) is Claim.provider (1..1) and gives the NPI
        if (string.IsNullOrWhiteSpace(request.OrderLocationCode))
        {
            problems.Add("orderLocationCode is required.");
        }

        // required by the gateway (Claim.careTeam is 0..* in FHIR R4) so every request names its clinician
        if (string.IsNullOrWhiteSpace(request.RequestingPractitionerId))
        {
            problems.Add("requestingPractitionerId is required.");
        }

        if (request.Priority != PasCodes.PriorityRoutine && request.Priority != PasCodes.PriorityUrgent)
        {
            problems.Add("priority must be 'routine' or 'urgent'.");
        }

        if (request.RequestType != PasCodes.RequestTypeNew && request.RequestType != PasCodes.RequestTypeRenewal)
        {
            problems.Add("requestType must be 'new' or 'renewal'.");
        }

        if (request.PatientType != PasCodes.PatientTypePermanent && request.PatientType != PasCodes.PatientTypeTransient)
        {
            problems.Add("patientType must be 'permanent' or 'transient'.");
        }

        if (request.PreviousTreatments != null && request.PreviousTreatments.Value < 0)
        {
            problems.Add("previousTreatments cannot be negative.");
        }

        // includeDtr = false means "no DTR forms": naming forms at the same time is a contradiction, not something to guess
        if (!request.IncludeDtr && TextHelper.Distinct(request.QuestionnaireResponseIds).Count > 0)
        {
            problems.Add("questionnaireResponseIds cannot be sent when includeDtr is false.");
        }

        if (problems.Count > 0)
        {
            throw new ApiException(400, "The request is not valid.", problems);
        }
    }

    /// <summary>
    /// Stores the request / response Bundles and the new decision of a request the payer accepted.
    /// </summary>
    /// <param name="record">The request; its decision, status and Bundles are replaced.</param>
    /// <param name="sent">The Bundle that was sent (the Claim is its first entry).</param>
    /// <param name="result">The payer's answer.</param>
    /// <param name="claimResponse">The ClaimResponse found in the answer.</param>
    /// <param name="wasCancel">
    /// True when the Bundle asked the payer to cancel the whole request. When the payer answers "complete" without
    /// sending line decisions, the request is cancelled (there are no lines that could say so). With line decisions,
    /// or with any other outcome (for example "queued": the payer has not processed the cancellation yet), the
    /// status comes from the payer's answer as usual.
    /// </param>
    private async Task ApplyResponseAsync(PriorAuthRecord record, Bundle sent, PayerCallResult result, ClaimResponse claimResponse, bool wasCancel)
    {
        Claim? claim = FhirJson.Read<Claim>(sent.Entry[0].Resource);
        record.Decision = PasDecisionReader.Read(claimResponse, claim ?? new Claim(), record, _workflow);
        if (wasCancel && record.Decision.Lines.Count == 0 && claimResponse.Outcome == "complete")
        {
            record.Decision.OverallStatus = DecisionStatus.Cancelled;
            record.Decision.DecidedAt = FhirDate.Now();
        }
        record.Status = record.Decision.OverallStatus;
        record.AuthorizationNumber = record.Decision.AuthorizationNumber;
        record.LastRequestBundleJson = FhirJson.Serialize(sent);
        record.LastResponseBundleJson = result.Body == null ? null : result.Body.ToJsonString();
        // The payer has accepted the request. The save is not cancelled when the caller disconnects: without the
        // row the gateway would not know the request and a retry would submit it a second time.
        await _repository.SaveAsync(record, CancellationToken.None);
        _logger.LogInformation("PAS {RequestId} v{Version} at {Payer}: {Status}", record.RequestId, record.Version, record.PayerName, record.Status);
    }

    /// <summary>
    /// The forms named in the request, plus completed forms already saved for these orders (DTR, via qr-context).
    /// </summary>
    private async Task<List<JsonObject>> ReadFormsAsync(PriorAuthRequest request, List<string> orderIds, CancellationToken ct)
    {
        List<JsonObject> forms = new List<JsonObject>();
        List<string> added = new List<string>();

        // The EHR said the orders need no DTR (CRD callDtr = false): no QuestionnaireResponse read or search at all.
        if (!request.IncludeDtr)
        {
            return forms;
        }

        foreach (string formId in TextHelper.Distinct(request.QuestionnaireResponseIds))
        {
            JsonObject? form = await _fhir.ReadAsync("QuestionnaireResponse", formId, ct);
            if (form == null)
            {
                throw new ApiException(404, "QuestionnaireResponse " + formId + " was not found at the FHIR API.");
            }
            forms.Add(form);
            added.Add(formId);
        }

        Bundle saved = await _fhir.SearchAsync("QuestionnaireResponse?patient=" + Uri.EscapeDataString(request.PatientId), ct);
        foreach (JsonObject form in saved.ResourcesOfType("QuestionnaireResponse"))
        {
            string? id = FhirJson.Id(form);
            if (id == null || added.Contains(id))
            {
                continue;
            }
            string? status = FhirJson.GetString(form, "status");
            bool finished = status == "completed" || status == "amended";
            if (!finished)
            {
                continue;
            }
            // attached when the form is for at least one of the orders of this request
            bool forTheseOrders = false;
            foreach (string formOrderId in PasBundleBuilder.FormOrderIds(form))
            {
                if (orderIds.Contains(formOrderId))
                {
                    forTheseOrders = true;
                }
            }
            if (forTheseOrders)
            {
                forms.Add(form);
                added.Add(id);
            }
        }
        return forms;
    }

    /// <summary>Reads each document with its file embedded; a missing document is an error (404).</summary>
    private async Task<List<JsonObject>> ReadDocumentsAsync(List<string>? documentIds, CancellationToken ct)
    {
        List<JsonObject> documents = new List<JsonObject>();
        foreach (string documentId in TextHelper.Distinct(documentIds))
        {
            JsonObject? document = await _fhir.ReadDocumentWithContentAsync(documentId, ct);
            if (document == null)
            {
                throw new ApiException(404, "Document " + documentId + " was not found at the FHIR API.");
            }
            documents.Add(document);
        }
        return documents;
    }

    /// <summary>Keeps existing line numbers and gives new orders the next free numbers (1, 2, 3 ...).</summary>
    private static Dictionary<string, int> AssignLineNumbers(Dictionary<string, int> existing, List<string> orderIds)
    {
        Dictionary<string, int> numbers = new Dictionary<string, int>(existing);
        int highest = 0;
        foreach (int number in numbers.Values)
        {
            if (number > highest)
            {
                highest = number;
            }
        }
        foreach (string orderId in orderIds)
        {
            if (!numbers.ContainsKey(orderId))
            {
                highest = highest + 1;
                numbers[orderId] = highest;
            }
        }
        return numbers;
    }

    /// <summary>The NPI of an Organization or Practitioner, or null.</summary>
    private static string? ReadNpi(JsonObject resource)
    {
        IdentifiedResource? typed = FhirJson.Read<IdentifiedResource>(resource);
        return typed == null ? null : typed.IdentifierValue(CodeSystems.Npi);
    }

    /// <summary>The Claim of a stored request Bundle.</summary>
    private static Claim ReadClaim(string bundleJson)
    {
        Bundle? bundle = FhirJson.Deserialize<Bundle>(bundleJson);
        if (bundle == null || bundle.Entry.Count == 0)
        {
            return new Claim();
        }
        Claim? claim = FhirJson.Read<Claim>(bundle.Entry[0].Resource);
        return claim ?? new Claim();
    }

    /// <summary>The "type" of a notification (from its SubscriptionStatus Parameters): handshake | heartbeat | event-notification.</summary>
    private static string NotificationType(JsonObject? body)
    {
        Bundle? bundle = FhirJson.Read<Bundle>(body);
        if (bundle == null)
        {
            return "event-notification";
        }
        foreach (JsonObject raw in bundle.ResourcesOfType("Parameters"))
        {
            Parameters? status = FhirJson.Read<Parameters>(raw);
            if (status == null)
            {
                continue;
            }
            foreach (ParametersParameter parameter in status.Parameter)
            {
                if (parameter.Name == "type" && parameter.ValueCode != null)
                {
                    return parameter.ValueCode;
                }
            }
        }
        return "event-notification";
    }

    /// <summary>The stored request, or 404.</summary>
    private async Task<PriorAuthRecord> GetRecordAsync(string requestId, CancellationToken ct)
    {
        PriorAuthRecord? record = await _repository.GetAsync(requestId, ct);
        if (record == null)
        {
            throw new ApiException(404, "Request " + requestId + " was not found.");
        }
        return record;
    }

    /// <summary>The request's payer, or 422 when it was removed or switched off.</summary>
    private async Task<PayerSettings> GetPayerAsync(PriorAuthRecord record, CancellationToken ct)
    {
        PayerSettings? payer = await _payers.FindAsync(record.PayerName, ct);
        if (payer == null)
        {
            throw new ApiException(422, "Payer " + record.PayerName + " is no longer configured.");
        }
        return payer;
    }

}
