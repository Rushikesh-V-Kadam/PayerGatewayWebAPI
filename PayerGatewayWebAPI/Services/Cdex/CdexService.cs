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
using PayerGatewayWebAPI.Services.Pas;
using PayerGatewayWebAPI.Services.Payers;

namespace PayerGatewayWebAPI.Services.Cdex;

/// <summary>
/// STEP 4 - CDex (Clinical Data Exchange) attachments. Only used when the payer pends a prior auth and needs more
/// (the first clinical documents are already sent with PAS).
///
///   1. The payer creates a Task: LOINC document types and/or questionnaires needed, a tracking id and a due date.
///   2. The gateway gets the Task - by searching the payer (GET {fhir}/Task?owner={organization NPI}), or the payer
///      pushes it to /api/v1/cdex/tasks/{payerName} - adds it to the decision's informationRequests and saves it
///      (the EHR reads it from table PA_PRIOR_AUTH, column DECISION_JSON).
///   3. The EHR sends the documents / completed forms -> POST {fhir}/$submit-attachment with the tracking id.
///   4. The payer reviews again; the gateway runs $inquire and returns the new decision.
/// </summary>
public class CdexService
{
    private readonly PayerRegistry _payers;
    private readonly PayerClient _payerClient;
    private readonly FhirApiClient _fhir;
    private readonly IPriorAuthRepository _requests;
    private readonly ICdexTaskRepository _tasks;
    private readonly PasService _pas;
    private readonly WorkflowSettings _workflow;
    private readonly ILogger<CdexService> _logger;

    /// <summary>Created by dependency injection.</summary>
    public CdexService(PayerRegistry payers, PayerClient payerClient, FhirApiClient fhir, IPriorAuthRepository requests,
        ICdexTaskRepository tasks, PasService pas, IOptions<WorkflowSettings> workflow, ILogger<CdexService> logger)
    {
        _payers = payers;
        _payerClient = payerClient;
        _fhir = fhir;
        _requests = requests;
        _tasks = tasks;
        _pas = pas;
        _workflow = workflow.Value;
        _logger = logger;
    }

    // ================================================================== 1-2. receive Tasks

    /// <summary>
    /// The information requests of a prior auth request. refresh = true searches the payer's Tasks now
    /// (instead of waiting for the background poller or a push).
    /// </summary>
    public async Task<List<InformationRequest>> GetInformationRequestsAsync(string requestId, bool refresh, CancellationToken ct)
    {
        PriorAuthRecord record = await GetRecordAsync(requestId, ct);
        if (refresh && record.IsOpen() && record.OrganizationNpi != null)
        {
            await PollTasksAsync(record.PayerName, record.OrganizationNpi, ct);
            record = await GetRecordAsync(requestId, ct);
        }
        return record.Decision.InformationRequests;
    }

    /// <summary>Searches the payer's Tasks for our organization and processes each one. Returns how many matched a request.</summary>
    public async Task<int> PollTasksAsync(string payerName, string organizationNpi, CancellationToken ct)
    {
        PayerSettings? payer = await _payers.FindAsync(payerName, ct);
        if (payer == null)
        {
            return 0;
        }

        PayerCallResult result = await _payerClient.GetFhirAsync(payer, "Task?owner=" + Uri.EscapeDataString(organizationNpi), ct);
        if (!result.IsSuccess())
        {
            _logger.LogWarning("Task search at {Payer} failed: HTTP {Status}", payerName, result.StatusCode);
            return 0;
        }

        Bundle? bundle;
        try
        {
            bundle = FhirJson.Read<Bundle>(result.Body);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "The Task search answer of {Payer} is not a readable FHIR Bundle", payerName);
            return 0;
        }
        if (bundle == null)
        {
            return 0;
        }

        int matched = 0;
        foreach (JsonObject raw in bundle.ResourcesOfType("Task"))
        {
            FhirTask? task = ReadTask(raw);
            if (task == null)
            {
                _logger.LogWarning("A Task of {Payer} is skipped: it is not a readable FHIR Task", payerName);
                continue;
            }
            if (await ProcessTaskAsync(payer, task, raw, ct))
            {
                matched++;
            }
        }
        return matched;
    }

    /// <summary>A Task (or a Bundle of Tasks) pushed by the payer.</summary>
    public async Task<NotificationResult> ReceivePushedTasksAsync(string payerName, JsonObject? body, CancellationToken ct)
    {
        PayerSettings payer = await _payers.GetRequiredAsync(payerName, 404, ct);

        List<JsonObject> tasks = new List<JsonObject>();
        string? type = FhirJson.ResourceType(body);
        if (type == "Task" && body != null)
        {
            tasks.Add(body);
        }
        else if (type == "Bundle")
        {
            try
            {
                Bundle? bundle = FhirJson.Read<Bundle>(body);
                if (bundle != null)
                {
                    tasks.AddRange(bundle.ResourcesOfType("Task"));
                }
            }
            catch (JsonException ex)
            {
                // the sender's mistake, so 400 (a payer must not keep re-sending the same body)
                throw new ApiException(400, "The Bundle is not valid FHIR: " + ex.Message);
            }
        }
        else
        {
            throw new ApiException(400, "A Task or a Bundle of Tasks is expected.");
        }

        int matched = 0;
        foreach (JsonObject raw in tasks)
        {
            FhirTask? task = ReadTask(raw);
            if (task == null)
            {
                throw new ApiException(400, "A Task in the body is not valid FHIR (an element has the wrong type).");
            }
            if (await ProcessTaskAsync(payer, task, raw, ct))
            {
                matched++;
            }
        }
        if (matched == 0)
        {
            throw new ApiException(422, "The Task does not match a prior authorization request sent by this gateway.");
        }

        NotificationResult outcome = new NotificationResult();
        outcome.Status = "accepted";
        outcome.Count = matched;
        return outcome;
    }

    /// <summary>The typed view of a raw Task, or null when the JSON does not have the shape of a FHIR Task.</summary>
    private static FhirTask? ReadTask(JsonObject raw)
    {
        try
        {
            return FhirJson.Read<FhirTask>(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Stores the Task and updates the request's informationRequests (saved with the request).
    /// Returns false when the Task is not about a request of this gateway and this payer.
    /// </summary>
    /// <param name="payer">The payer the Task came from.</param>
    /// <param name="task">The typed view of the Task.</param>
    /// <param name="raw">The Task exactly as received (stored in PA_CDEX_TASK.TASK_JSON).</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    private async Task<bool> ProcessTaskAsync(PayerSettings payer, FhirTask task, JsonObject raw, CancellationToken ct)
    {
        if (task.Id == null)
        {
            return false;
        }
        string? requestId = TaskReader.RequestId(task);
        Identifier? tracking = TaskReader.TrackingIdentifier(task);
        PriorAuthRecord? record = requestId == null ? null : await _requests.GetAsync(requestId, ct);
        if (record == null || tracking == null || tracking.Value == null
            || !string.Equals(record.PayerName, payer.PayerName, StringComparison.OrdinalIgnoreCase))
        {
            return false;   // not one of ours
        }

        // One information request per tracking id on the decision
        InformationRequest info = TaskReader.ToInformationRequest(task);
        List<InformationRequest> list = record.Decision.InformationRequests;
        int index = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].TrackingId == info.TrackingId)
            {
                index = i;
                break;
            }
        }

        // The same Task is seen again on every poll. Each of the two rows is saved only when it really changed, so
        // RECEIVED_AT of PA_CDEX_TASK and UPDATED_AT of PA_PRIOR_AUTH keep meaning "something changed".

        // ---- the Task row (PA_CDEX_TASK): new, or the payer changed anything in the Task
        string taskJson = raw.ToJsonString();
        CdexTaskRecord? storedTask = null;
        foreach (CdexTaskRecord candidate in await _tasks.GetForRequestAsync(record.RequestId, ct))
        {
            if (candidate.TaskId == task.Id && string.Equals(candidate.PayerName, payer.PayerName, StringComparison.OrdinalIgnoreCase))
            {
                storedTask = candidate;
            }
        }
        if (storedTask == null || storedTask.TaskJson != taskJson)
        {
            CdexTaskRecord taskRecord = new CdexTaskRecord();
            taskRecord.PayerName = payer.PayerName;
            taskRecord.TaskId = task.Id;
            taskRecord.RequestId = record.RequestId;
            taskRecord.TrackingId = tracking.Value;
            taskRecord.TrackingSystem = tracking.System;
            taskRecord.Status = task.Status;
            if (task.Restriction != null && task.Restriction.Period != null)
            {
                taskRecord.DueDate = task.Restriction.Period.End;
            }
            taskRecord.TaskJson = taskJson;
            await _tasks.SaveAsync(taskRecord, ct);
        }

        // ---- the information request on the decision (PA_PRIOR_AUTH): new, or different from what is stored.
        // Compared as JSON so every detail counts (status, due date, documents).
        bool changed = index < 0 || ApiJson.Write(list[index]) != ApiJson.Write(info);
        if (!changed)
        {
            return true;
        }
        if (index < 0)
        {
            list.Add(info);
        }
        else
        {
            list[index] = info;
        }
        _logger.LogInformation("Information request {TrackingId} for {RequestId}: {Status}", info.TrackingId, record.RequestId, info.Status);
        await _requests.SaveAsync(record, ct);
        return true;
    }

    // ================================================================== 3. $submit-attachment

    /// <summary>Sends additional documents and / or completed forms to the payer for a pended prior auth request.</summary>
    public async Task<AttachmentResult> SubmitAttachmentsAsync(string requestId, AttachmentSubmitRequest request, CancellationToken ct)
    {
        PriorAuthRecord record = await GetRecordAsync(requestId, ct);
        if (TextHelper.Distinct(request.DocumentIds).Count == 0 && TextHelper.Distinct(request.QuestionnaireResponseIds).Count == 0)
        {
            throw new ApiException(400, "Send at least one documentId or questionnaireResponseId.");
        }
        if (string.IsNullOrWhiteSpace(record.MemberId))
        {
            throw new ApiException(422, "Request " + requestId + " has no member id (PA_PRIOR_AUTH.MEMBER_ID), so the attachments cannot be sent: the payer needs it to find the member.");
        }
        PayerSettings? payer = await _payers.FindAsync(record.PayerName, ct);
        if (payer == null)
        {
            throw new ApiException(422, "Payer " + record.PayerName + " is no longer configured.");
        }

        Identifier trackingId = await ChooseTrackingIdAsync(record, request.TrackingId, ct);
        Parameters parameters = BuildHeader(record, trackingId);

        // attachments: documents (file embedded) and forms
        int count = 0;
        foreach (string documentId in TextHelper.Distinct(request.DocumentIds))
        {
            JsonObject? document = await _fhir.ReadDocumentWithContentAsync(documentId, ct);
            if (document == null)
            {
                throw new ApiException(404, "Document " + documentId + " was not found at the FHIR API.");
            }
            Coding? loinc = null;
            DocumentReference? typed = FhirJson.Read<DocumentReference>(document);
            if (typed != null && typed.Type != null)
            {
                loinc = typed.Type.CodingWithSystem(CodeSystems.Loinc);
            }
            parameters.Parameter.Add(AttachmentPart(document, loinc, request.LineNumbers));
            count++;
        }
        foreach (string formId in TextHelper.Distinct(request.QuestionnaireResponseIds))
        {
            JsonObject? form = await _fhir.ReadAsync("QuestionnaireResponse", formId, ct);
            if (form == null)
            {
                throw new ApiException(404, "QuestionnaireResponse " + formId + " was not found at the FHIR API.");
            }
            parameters.Parameter.Add(AttachmentPart(form, new Coding(CodeSystems.Loinc, CdexCodes.PayerQuestionnaireLoinc, null), request.LineNumbers));
            count++;
        }

        ParametersParameter final = new ParametersParameter();
        final.Name = CdexCodes.Final;
        final.ValueBoolean = request.Final;
        parameters.Parameter.Add(final);

        // send
        PayerCallResult result = await _payerClient.PostFhirAsync(payer, CdexCodes.SubmitAttachment, parameters, ct);
        AttachmentResult summary = new AttachmentResult();
        summary.RequestId = requestId;
        summary.TrackingId = trackingId.Value ?? string.Empty;
        summary.AttachmentCount = count;
        summary.Status = result.IsSuccess() ? "sent" : "rejected";
        if (result.Outcome != null)
        {
            summary.Messages = result.Outcome.Messages();
        }
        if (!result.IsSuccess())
        {
            int status = result.StatusCode >= 400 && result.StatusCode < 500 ? 422 : 502;
            summary.Messages = result.Messages();
            throw new ApiException(status, string.Join("; ", summary.Messages), summary);
        }

        // Mark the information request answered, then ask the payer for the new decision
        foreach (InformationRequest info in record.Decision.InformationRequests)
        {
            if (info.TrackingId == summary.TrackingId)
            {
                info.Status = request.Final ? "completed" : "in-progress";
            }
        }
        // the payer has the attachments: this is saved even when the caller has stopped waiting
        await _requests.SaveAsync(record, CancellationToken.None);

        if (request.Final)
        {
            // An extra step: when it fails, the attachments were still sent. The decision is then left empty in
            // the answer and the background poller (or POST .../inquire) gets it later.
            summary.Decision = await _pas.TryInquireAsync(requestId, ct);
        }
        return summary;
    }

    // ================================================================== helpers

    /// <summary>
    /// Tracking id to send: the one given, else the latest open information request, else our own request id
    /// (an unsolicited attachment - the payer matches it to the Claim identifier).
    /// </summary>
    private async Task<Identifier> ChooseTrackingIdAsync(PriorAuthRecord record, string? requested, CancellationToken ct)
    {
        string? trackingId = requested;
        if (string.IsNullOrWhiteSpace(trackingId))
        {
            foreach (InformationRequest info in record.Decision.InformationRequests)
            {
                if (info.Status != "completed" && info.Status != "cancelled")
                {
                    trackingId = info.TrackingId;   // the last open one wins
                }
            }
        }
        if (string.IsNullOrWhiteSpace(trackingId) || trackingId == record.RequestId)
        {
            return new Identifier(SystemOrNull(_workflow.ClaimIdentifierSystem), record.RequestId);
        }

        List<CdexTaskRecord> tasks = await _tasks.GetForRequestAsync(record.RequestId, ct);
        foreach (CdexTaskRecord task in tasks)
        {
            if (task.TrackingId == trackingId)
            {
                return new Identifier(SystemOrNull(task.TrackingSystem), task.TrackingId);
            }
        }
        throw new ApiException(400, "Tracking id " + trackingId + " is not an information request of " + record.RequestId + ".");
    }

    /// <summary>
    /// The $submit-attachment header parameters: TrackingId, AttachTo, PayerId, OrganizationId, ProviderId, MemberId.
    /// The payer id and the member id are the ones of the prior authorization request (read from the Coverage of the
    /// stored request Bundle, with the system they had there), so the payer can match the attachment to that request.
    /// PayerId is optional in CDex and is left out when the insurance has no payer id - the request then had no
    /// insurer identifier either. MemberId is required and always sent (the caller has already checked that the
    /// request has a member id); it has no system when the insurance gives none.
    /// </summary>
    private static Parameters BuildHeader(PriorAuthRecord record, Identifier trackingId)
    {
        Coverage? sentCoverage = SentCoverage(record);
        Parameters parameters = new Parameters();
        parameters.Meta = new Meta();
        parameters.Meta.Profile.Add(CdexCodes.ParametersProfile);

        parameters.Parameter.Add(IdentifierParameter(CdexCodes.TrackingId, trackingId));

        ParametersParameter attachTo = new ParametersParameter();
        attachTo.Name = CdexCodes.AttachTo;
        attachTo.ValueCode = CdexCodes.PreAuthorization;
        parameters.Parameter.Add(attachTo);

        Identifier? payerId = null;
        if (sentCoverage != null)
        {
            payerId = sentCoverage.PayorIdentifier();
        }
        if (payerId != null && !string.IsNullOrWhiteSpace(payerId.Value))
        {
            parameters.Parameter.Add(IdentifierParameter(CdexCodes.PayerId, new Identifier(SystemOrNull(payerId.System), payerId.Value)));
        }
        if (record.OrganizationNpi != null)
        {
            parameters.Parameter.Add(IdentifierParameter(CdexCodes.OrganizationId, new Identifier(CodeSystems.Npi, record.OrganizationNpi)));
        }
        if (record.PractitionerNpi != null)
        {
            parameters.Parameter.Add(IdentifierParameter(CdexCodes.ProviderId, new Identifier(CodeSystems.Npi, record.PractitionerNpi)));
        }
        // the member id with the system it had in the request (no system when the insurance gives none)
        string? memberIdSystem = null;
        if (sentCoverage != null)
        {
            Identifier? memberIdentifier = sentCoverage.MemberIdentifier();
            if (memberIdentifier != null && memberIdentifier.Value == record.MemberId)
            {
                memberIdSystem = SystemOrNull(memberIdentifier.System);
            }
        }
        parameters.Parameter.Add(IdentifierParameter(CdexCodes.MemberId, new Identifier(memberIdSystem, record.MemberId)));
        return parameters;
    }

    /// <summary>
    /// The Coverage that was sent to the payer in the last prior authorization request of this record,
    /// or null when the stored request Bundle is missing or has no Coverage.
    /// </summary>
    private static Coverage? SentCoverage(PriorAuthRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.LastRequestBundleJson))
        {
            return null;
        }

        Bundle? bundle;
        try
        {
            bundle = FhirJson.Deserialize<Bundle>(record.LastRequestBundleJson);
        }
        catch (JsonException)
        {
            return null;   // the stored text is not a Bundle: send the ids without their systems
        }
        if (bundle == null)
        {
            return null;
        }

        foreach (JsonObject raw in bundle.ResourcesOfType("Coverage"))
        {
            Coverage? coverage = FhirJson.Read<Coverage>(raw);
            if (coverage != null)
            {
                return coverage;
            }
        }
        return null;
    }

    /// <summary>An identifier system, or null when it is empty (an empty "system" must not be sent).</summary>
    private static string? SystemOrNull(string? system)
    {
        if (string.IsNullOrWhiteSpace(system))
        {
            return null;
        }
        return system;
    }

    /// <summary>A parameter holding an Identifier.</summary>
    private static ParametersParameter IdentifierParameter(string name, Identifier value)
    {
        ParametersParameter parameter = new ParametersParameter();
        parameter.Name = name;
        parameter.ValueIdentifier = value;
        return parameter;
    }

    /// <summary>One "Attachment" parameter: line numbers, LOINC code and the resource itself.</summary>
    private static ParametersParameter AttachmentPart(JsonObject content, Coding? loinc, List<int>? lineNumbers)
    {
        ParametersParameter part = new ParametersParameter();
        part.Name = CdexCodes.Attachment;

        // no line numbers (an empty or missing list) = the attachment is for the whole request
        List<int> lines = lineNumbers ?? new List<int>();
        List<int> added = new List<int>();
        foreach (int line in lines)
        {
            if (added.Contains(line))
            {
                continue;
            }
            added.Add(line);
            ParametersParameter lineItem = new ParametersParameter();
            lineItem.Name = CdexCodes.LineItem;
            lineItem.ValueString = line.ToString();
            part.Part.Add(lineItem);
        }
        if (loinc != null && loinc.Code != null)
        {
            ParametersParameter code = new ParametersParameter();
            code.Name = CdexCodes.Code;
            code.ValueCodeableConcept = new CodeableConcept(CodeSystems.Loinc, loinc.Code, loinc.Display);
            part.Part.Add(code);
        }
        ParametersParameter resource = new ParametersParameter();
        resource.Name = CdexCodes.Content;
        resource.Resource = FhirJson.Copy(content);
        part.Part.Add(resource);
        return part;
    }

    /// <summary>The stored request, or 404.</summary>
    private async Task<PriorAuthRecord> GetRecordAsync(string requestId, CancellationToken ct)
    {
        PriorAuthRecord? record = await _requests.GetAsync(requestId, ct);
        if (record == null)
        {
            throw new ApiException(404, "Request " + requestId + " was not found.");
        }
        return record;
    }
}
