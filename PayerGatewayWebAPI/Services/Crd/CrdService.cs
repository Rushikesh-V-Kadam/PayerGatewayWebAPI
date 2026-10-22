using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Constants;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Models.CdsHooks;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Models.Fhir;
using PayerGatewayWebAPI.Models.Requests;
using PayerGatewayWebAPI.Models.Responses;
using PayerGatewayWebAPI.Repositories.Interfaces;
using PayerGatewayWebAPI.Services.Pas;
using PayerGatewayWebAPI.Services.Payers;

namespace PayerGatewayWebAPI.Services.Crd;

/// <summary>
/// STEP 1 - CRD (Coverage Requirements Discovery), order-sign only. Called when the clinician signs orders.
///
///   1. find the payer by payerName (PayerRegistry: table PA_PAYER, or appsettings "Payers" in memory mode)
///   2. read the patient, the orders and the coverage from the FHIR API
///   3. get the payer's CDS services (discovery, cached 12 h) -> the order-sign service id + prefetch templates
///   4. call POST {payer}/cds-services/{id} with context, prefetch and a fhirAuthorization token
///   5. turn the answer into one CoverageDecision per order (pa-needed, doc-needed, questionnaires, callDtr, callPas)
///   6. save each decision (the EHR reads it from table PA_COVERAGE_DECISION) and return them
/// </summary>
public class CrdService
{
    private readonly FhirApiClient _fhir;
    private readonly PayerRegistry _payers;
    private readonly PayerClient _payerClient;
    private readonly CdsHookBuilder _builder;
    private readonly ICrdRepository _crdRepository;
    private readonly IDtrLinkRepository _linkRepository;
    private readonly WorkflowSettings _workflow;
    private readonly ILogger<CrdService> _logger;

    /// <summary>Created by dependency injection.</summary>
    public CrdService(FhirApiClient fhir, PayerRegistry payers, PayerClient payerClient, CdsHookBuilder builder,
        ICrdRepository crdRepository, IDtrLinkRepository linkRepository, IOptions<WorkflowSettings> workflow, ILogger<CrdService> logger)
    {
        _fhir = fhir;
        _payers = payers;
        _payerClient = payerClient;
        _builder = builder;
        _crdRepository = crdRepository;
        _linkRepository = linkRepository;
        _workflow = workflow.Value;
        _logger = logger;
    }

    // ================================================================== entry point

    /// <summary>Runs order-sign for one EHR event.</summary>
    public async Task<OrderSignResponse> OrderSignAsync(OrderSignRequest request, CancellationToken ct)
    {
        // ---- check the input
        List<string> problems = new List<string>();
        if (string.IsNullOrWhiteSpace(request.EventId))
        {
            problems.Add("eventId is required.");
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

        // CDS Hooks order-sign context.userId is 1..1
        if (string.IsNullOrWhiteSpace(request.UserPractitionerId))
        {
            problems.Add("userPractitionerId is required.");
        }

        // CLIENT-EHR: the EHR sends the patient's active insurance; the orders carry no insurance.
        if (string.IsNullOrWhiteSpace(request.CoverageId))
        {
            problems.Add("coverageId is required.");
        }

        if (problems.Count > 0)
        {
            throw new ApiException(400, "The request is not valid.", problems);
        }

        // ---- 1. the payer
        PayerSettings payer = await _payers.GetRequiredAsync(request.PayerName, 400, ct);

        // Same eventId again -> same answer, no second payer call (the EHR may retry)
        CrdEventRecord? previous = await _crdRepository.GetEventAsync(request.EventId, ct);
        if (previous != null)
        {
            OrderSignResponse? stored = JsonSerializer.Deserialize<OrderSignResponse>(previous.ResponseJson, ApiJson.Options);
            if (stored != null)
            {
                return stored;
            }
        }

        // ---- 2. data from the FHIR API
        JsonObject? patient = await _fhir.ReadAsync("Patient", request.PatientId, ct);
        if (patient == null)
        {
            throw new ApiException(404, "Patient " + request.PatientId + " was not found at the FHIR API.");
        }

        OrderSignResponse response = new OrderSignResponse();
        response.EventId = request.EventId;
        response.PayerName = payer.PayerName;
        response.PayerCall.PayerName = payer.PayerName;

        List<JsonObject> orders = await ReadOrdersAsync(request, response, ct);
        if (orders.Count == 0)
        {
            throw new ApiException(404, "None of the orders in orderIds were found at the FHIR API.");
        }

        // The signing clinician (CDS Hooks context.userId, 1..1): always the one in the request (checked above).
        string userPractitionerId = request.UserPractitionerId.Trim();

        // CLIENT-EHR: no facility is sent in order-sign. The request has no facility field and the client's orders carry
        // none (the facility / performer is not required by CDS Hooks or FHIR R4); the orders go to the payer as they are.

        // CLIENT-EHR: the insurance is the one the EHR sends (coverageId), read by its id at the FHIR API.
        string coverageId = request.CoverageId.Trim();
        Coverage? coverage = await _fhir.ReadCoverageAsync(coverageId, ct);
        string coverageProblem = OrderHelper.CoverageProblem(coverage, coverageId, request.PatientId);

        // ---- 3-5. ask the payer (or explain why it could not be asked)
        // An insurance that cannot be used does not stop the signing: every order gets "unknown" with the reason.
        if (coverage == null || coverageProblem.Length > 0)
        {
            response.PayerCall.Outcome = "skipped";
            response.PayerCall.Error = coverageProblem;
            AddUnknownDecisions(response, orders, payer, coverageId, coverageProblem + " Coverage requirements could not be checked.");
        }
        else
        {
            await CallPayerAsync(request, payer, coverage, orders, userPractitionerId, response, ct);
        }

        // ---- 6. save (the EHR reads the decision from the same table; no separate write-back)
        foreach (CoverageDecision decision in response.Decisions)
        {
            CoverageDecisionReader.SetNextSteps(decision);

            CoverageDecisionRecord record = new CoverageDecisionRecord();
            record.OrderId = decision.OrderId;
            record.EventId = request.EventId;
            record.PayerName = decision.PayerName;
            record.PatientId = request.PatientId;
            record.CoverageAssertionId = decision.CoverageAssertionId;
            record.Covered = decision.Covered;
            record.PriorAuthNeeded = decision.PriorAuthNeeded;
            record.DocumentationNeeded = decision.DocumentationNeeded;
            record.DecisionJson = ApiJson.Write(decision);
            await _crdRepository.SaveDecisionAsync(record, ct);
        }

        CrdEventRecord eventRecord = new CrdEventRecord();
        eventRecord.EventId = request.EventId;
        eventRecord.PatientId = request.PatientId;
        eventRecord.PayerName = payer.PayerName;
        eventRecord.ResponseJson = ApiJson.Write(response);
        await _crdRepository.SaveEventAsync(eventRecord, ct);

        _logger.LogInformation("order-sign event {EventId} at {Payer}: {Count} decision(s)", request.EventId, payer.PayerName, response.Decisions.Count);
        return response;
    }

    /// <summary>Latest stored decision of an order (no payer call).</summary>
    public async Task<CoverageDecision> GetDecisionAsync(string orderId, CancellationToken ct)
    {
        CoverageDecisionRecord? record = await _crdRepository.GetDecisionAsync(orderId, ct);
        if (record == null)
        {
            throw new ApiException(404, "No coverage decision for order " + orderId + ". Call POST /api/v1/crd/order-sign first.");
        }
        CoverageDecision? decision = JsonSerializer.Deserialize<CoverageDecision>(record.DecisionJson, ApiJson.Options);
        if (decision == null)
        {
            throw new ApiException(500, "The stored decision of order " + orderId + " could not be read.");
        }
        return decision;
    }

    // ================================================================== 2. orders

    /// <summary>Reads the orders; a missing order becomes a warning, an order of another patient an error (400).</summary>
    private async Task<List<JsonObject>> ReadOrdersAsync(OrderSignRequest request, OrderSignResponse response, CancellationToken ct)
    {
        List<string> orderIds = TextHelper.Distinct(request.OrderIds);
        List<JsonObject> found = await _fhir.ReadOrdersAsync(orderIds, ct);
        List<JsonObject> orders = new List<JsonObject>();

        foreach (string orderId in orderIds)
        {
            JsonObject? raw = OrderHelper.FindOrder(found, orderId);
            if (raw == null)
            {
                EhrMessage message = new EhrMessage();
                message.Severity = "warning";
                message.Summary = "Order " + orderId + " was not found.";
                response.Messages.Add(message);
                continue;
            }
            if (OrderHelper.ReadOrder(raw).GetPatientId() != request.PatientId)
            {
                throw new ApiException(400, "Order " + orderId + " does not belong to patient " + request.PatientId + ".");
            }
            orders.Add(raw);
        }
        return orders;
    }

    // ================================================================== 3-5. the payer call

    /// <summary>
    /// Calls the payer's order-sign service and adds the decisions / messages to the response.
    /// A payer that cannot be reached, is too slow or sends an answer that cannot be read never fails the request:
    /// every order then gets an "unknown" decision and the reason is in response.PayerCall.
    /// </summary>
    private async Task CallPayerAsync(OrderSignRequest request, PayerSettings payer, Coverage coverage,
        List<JsonObject> orders, string user, OrderSignResponse response, CancellationToken ct)
    {
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            // ---- 3. discovery: which CDS service handles order-sign (cached 12 h).
            // The clinician is waiting, so the short CRD timeout is used for this call too.
            CdsDiscoveryResponse? discovery = _payers.GetCachedDiscovery(payer);
            if (discovery == null)
            {
                discovery = await _payerClient.DiscoverAsync(payer, payer.CrdTimeoutSeconds, ct);
                _payers.CacheDiscovery(payer, discovery);
            }

            CdsServiceDefinition? service = FindOrderSignService(discovery);
            if (service == null)
            {
                response.PayerCall.Outcome = "skipped";
                response.PayerCall.Error = payer.GetDisplayName() + " has no CDS service for order-sign.";
                AddUnknownDecisions(response, orders, payer, coverage.Id, response.PayerCall.Error);
                return;
            }
            response.PayerCall.ServiceId = service.Id;

            // ---- 4. the hook call
            CdsHookRequest hookRequest = new CdsHookRequest();
            hookRequest.Hook = CrdCodes.OrderSignHook;
            List<JsonObject> sentOrders = orders;
            hookRequest.FhirServer = _fhir.GetBaseUrl();
            hookRequest.FhirAuthorization = await TryGetFhirAuthorizationAsync(payer, request.PatientId, ct);
            hookRequest.Context = _builder.BuildContext(request, sentOrders, user);
            if (service.Prefetch != null && service.Prefetch.Count > 0)
            {
                hookRequest.Prefetch = await _builder.BuildPrefetchAsync(service.Prefetch, request, sentOrders, user, ct);
            }

            CdsHookResponse hookResponse = await _payerClient.CallHookAsync(payer, service.Id, hookRequest, ct);

            // ---- 5a. coverage information -> one decision per order
            Dictionary<string, CoverageDecision> byReference = CoverageDecisionReader.Read(hookResponse);
            foreach (JsonObject raw in orders)
            {
                OrderResource order = OrderHelper.ReadOrder(raw);
                CoverageDecision? decision;
                if (byReference.TryGetValue(order.GetReference(), out decision))
                {
                    decision.OrderId = order.Id ?? string.Empty;
                    decision.PayerName = payer.PayerName;
                    if (decision.CoverageId == null)
                    {
                        decision.CoverageId = coverage.Id;
                    }
                    response.Decisions.Add(decision);
                }
                else
                {
                    response.Decisions.Add(UnknownDecision(order.Id ?? string.Empty, payer, coverage.Id,
                        payer.GetDisplayName() + " returned no coverage information for this order."));
                }
            }

            // ---- 5b. cards -> messages (on the order they mention, else on the response)
            foreach (CdsCard card in hookResponse.Cards)
            {
                string? orderId = OrderMentionedByCard(card, orders);
                EhrMessage message = await ToMessageAsync(card, payer, request, orders, orderId, coverage.Id, user, ct);
                CoverageDecision? owner = orderId == null ? null : FindDecision(response, orderId);
                if (owner != null)
                {
                    owner.Messages.Add(message);
                }
                else
                {
                    response.Messages.Add(message);
                }
            }
        }
        catch (PayerException ex)
        {
            _logger.LogWarning(ex, "order-sign call to {Payer} failed", payer.PayerName);
            response.PayerCall.Outcome = ex.StatusCode == 504 ? "timeout" : "error";
            response.PayerCall.Error = ex.Message;
            AddUnknownDecisions(response, orders, payer, coverage.Id,
                payer.GetDisplayName() + " did not answer the coverage check. Continue; requirements are checked again at prior authorization.");
        }
        catch (JsonException ex)
        {
            // The payer answered, but a part of the answer does not have the CRD shape (for example a broken
            // coverage-information extension). Treated like no answer: no half-read decision is returned.
            _logger.LogWarning(ex, "The order-sign answer of {Payer} could not be read", payer.PayerName);
            response.PayerCall.Outcome = "error";
            response.PayerCall.Error = payer.GetDisplayName() + " returned an answer that could not be read: " + ex.Message;
            response.Decisions.Clear();
            AddUnknownDecisions(response, orders, payer, coverage.Id,
                payer.GetDisplayName() + " returned a coverage answer that could not be read. Continue; requirements are checked again at prior authorization.");
        }
        finally
        {
            response.PayerCall.DurationMs = watch.ElapsedMilliseconds;
        }
    }

    /// <summary>The payer's CDS service for the order-sign hook, or null.</summary>
    private static CdsServiceDefinition? FindOrderSignService(CdsDiscoveryResponse discovery)
    {
        foreach (CdsServiceDefinition? service in discovery.Services)
        {
            // a service without an id cannot be called (its id is part of the URL)
            if (service != null && service.Hook == CrdCodes.OrderSignHook && !string.IsNullOrWhiteSpace(service.Id))
            {
                return service;
            }
        }
        return null;
    }

    /// <summary>fhirAuthorization lets the payer read more data during CRD. Optional: the hook is sent without it on failure.</summary>
    private async Task<JsonObject?> TryGetFhirAuthorizationAsync(PayerSettings payer, string patientId, CancellationToken ct)
    {
        try
        {
            return await _fhir.IssueAccessTokenAsync(payer.PayerName, patientId, _workflow.FhirAuthorizationMinutes, ct);
        }
        catch (FhirApiException ex)
        {
            _logger.LogWarning(ex, "No fhirAuthorization for {Payer}; hook sent without it", payer.PayerName);
            return null;
        }
    }

    // ================================================================== cards

    /// <summary>The order a card is about: the only order, or the one whose "Type/id" appears in the card. Null when unclear.</summary>
    private static string? OrderMentionedByCard(CdsCard card, List<JsonObject> orders)
    {
        if (orders.Count == 1)
        {
            return FhirJson.Id(orders[0]);
        }

        string text = card.Summary + " " + card.Detail;
        foreach (CdsLink link in card.Links)
        {
            text = text + " " + link.AppContext;
        }
        foreach (JsonObject raw in orders)
        {
            OrderResource order = OrderHelper.ReadOrder(raw);
            if (MentionsReference(text, order.GetReference()))
            {
                return order.Id;
            }
        }
        return null;
    }

    /// <summary>
    /// True when the text contains exactly this "Type/id". A longer id that only starts the same does not count:
    /// "ServiceRequest/ord-10" in the text is not a mention of "ServiceRequest/ord-1".
    /// </summary>
    private static bool MentionsReference(string text, string reference)
    {
        int position = text.IndexOf(reference, StringComparison.Ordinal);
        while (position >= 0)
        {
            int end = position + reference.Length;
            if (!IdGoesOn(text, end))
            {
                return true;
            }
            position = text.IndexOf(reference, end, StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>
    /// True when the id that ends just before <paramref name="position"/> continues there: with a letter or digit,
    /// or with "-" / "." followed by a letter or digit ("ord-1" continues in "ord-10" and in "ord-1-hd").
    /// A "." or "-" that is not followed by a letter or digit is normal punctuation ("... ServiceRequest/ord-1.").
    /// </summary>
    private static bool IdGoesOn(string text, int position)
    {
        if (position >= text.Length)
        {
            return false;
        }
        char next = text[position];
        if (char.IsLetterOrDigit(next))
        {
            return true;
        }
        if (next == '-' || next == '.')
        {
            return position + 1 < text.Length && char.IsLetterOrDigit(text[position + 1]);
        }
        return false;
    }

    /// <summary>
    /// Card -> EHR message. A SMART link (a payer DTR app) cannot be opened by the EHR directly, so the launch details are
    /// saved and the EHR gets a plain gateway URL: /api/v1/dtr/launch/{linkId}.
    /// </summary>
    private async Task<EhrMessage> ToMessageAsync(CdsCard card, PayerSettings payer, OrderSignRequest request, List<JsonObject> orders,
        string? orderId, string? coverageId, string userPractitionerId, CancellationToken ct)
    {
        EhrMessage message = new EhrMessage();
        message.Severity = card.Indicator;
        message.Summary = card.Summary;
        message.Detail = card.Detail;

        CdsLink? link = null;
        foreach (CdsLink candidate in card.Links)
        {
            if (candidate.Type == CrdCodes.LinkTypeSmart)
            {
                link = candidate;
                break;
            }
        }
        if (link == null && card.Links.Count > 0)
        {
            link = card.Links[0];
        }
        if (link == null)
        {
            return message;
        }

        message.Link = new EhrMessageLink();
        message.Link.Label = link.Label;
        if (link.Type != CrdCodes.LinkTypeSmart)
        {
            message.Link.Url = link.Url;
            message.Link.Type = CrdCodes.LinkTypeAbsolute;
            return message;
        }

        // SMART link: remember the launch context, give the EHR a gateway URL
        DtrLinkRecord dtrLink = new DtrLinkRecord();
        dtrLink.PayerName = payer.PayerName;
        dtrLink.PatientId = request.PatientId;
        dtrLink.EncounterId = request.EncounterId;
        dtrLink.UserPractitionerId = userPractitionerId;
        foreach (JsonObject raw in orders)
        {
            OrderResource order = OrderHelper.ReadOrder(raw);
            if (orderId == null || order.Id == orderId)
            {
                dtrLink.FhirContext.Add(order.GetReference());
            }
        }
        if (coverageId != null)
        {
            dtrLink.FhirContext.Add("Coverage/" + coverageId);
        }
        dtrLink.AppContext = link.AppContext;
        dtrLink.AppUrl = ChooseDtrAppUrl(payer, link.Url);
        dtrLink.ExpiresAt = DateTime.UtcNow.AddHours(_workflow.DtrLinkHours);
        await _linkRepository.SaveAsync(dtrLink, ct);

        message.Link.Url = _workflow.GetGatewayBaseUrl() + "/api/v1/dtr/launch/" + dtrLink.LinkId;
        message.Link.Type = CrdCodes.LinkTypeSmart;
        message.Link.OpensPayerApp = payer.UsesSmartApp();
        return message;
    }

    /// <summary>
    /// The DTR app to open: 1) the payer's fixed launch URL (PA_PAYER.DTR_APP_LAUNCH_URL), 2) a provider-wide app
    /// (Workflow:DtrAppUrl), 3) the URL on the payer's card (normal case for payers with their own app).
    /// </summary>
    private string ChooseDtrAppUrl(PayerSettings payer, string cardUrl)
    {
        if (!string.IsNullOrWhiteSpace(payer.DtrAppLaunchUrl))
        {
            return payer.DtrAppLaunchUrl;
        }
        if (!string.IsNullOrWhiteSpace(_workflow.DtrAppUrl))
        {
            return _workflow.DtrAppUrl;
        }
        return cardUrl;
    }

    // ================================================================== helpers

    /// <summary>The decision of an order in the response, or null.</summary>
    private static CoverageDecision? FindDecision(OrderSignResponse response, string orderId)
    {
        foreach (CoverageDecision decision in response.Decisions)
        {
            if (decision.OrderId == orderId)
            {
                return decision;
            }
        }
        return null;
    }

    /// <summary>Adds an "unknown" decision for every order (the payer could not be asked).</summary>
    private static void AddUnknownDecisions(OrderSignResponse response, List<JsonObject> orders, PayerSettings payer, string? coverageId, string text)
    {
        foreach (JsonObject raw in orders)
        {
            string orderId = FhirJson.Id(raw) ?? string.Empty;
            response.Decisions.Add(UnknownDecision(orderId, payer, coverageId, text));
        }
    }

    /// <summary>Decision used when the payer could not be asked (no insurance, no service, timeout).</summary>
    private static CoverageDecision UnknownDecision(string orderId, PayerSettings payer, string? coverageId, string text)
    {
        CoverageDecision decision = new CoverageDecision();
        decision.OrderId = orderId;
        decision.PayerName = payer.PayerName;
        decision.CoverageId = coverageId;
        decision.Covered = CrdCodes.Unknown;
        decision.DecidedAt = FhirDate.Now();

        EhrMessage message = new EhrMessage();
        message.Severity = "warning";
        message.Summary = TextHelper.Shorten(text, 140);
        message.Detail = text;
        decision.Messages.Add(message);
        return decision;
    }
}
