using System.Text.Json.Nodes;
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

namespace PayerGatewayWebAPI.Services.Dtr;

/// <summary>
/// STEP 2 - DTR (Documentation Templates and Rules). Used when order-sign says callDtr = true.
///
/// NOW (no UI and no payer app needed) - "forms":
///   1. CreateFormsAsync: calls the payer's $questionnaire-package for the orders and coverage, keeps each
///      Questionnaire as a form, and returns it as simple questions (QuestionnaireReader).
///   2. The EHR shows the questions on its own screen (or staff answer them in Swagger / Postman during testing).
///   3. SaveAnswersAsync: turns the plain answers into a FHIR QuestionnaireResponse (QuestionnaireResponseBuilder)
///      and saves it at the FHIR API. PAS attaches completed forms of the orders automatically.
///
/// PAYERS WITH THEIR OWN SMART DTR APP (PA_PAYER.DTR_MODE = SmartApp):
///   LaunchAsync opens the payer app from the CRD message link (SMART EHR launch at the FHIR API, FHIR R4);
///   the app saves its QuestionnaireResponse at the FHIR API; GetSavedFormsAsync lets the EHR see it;
///   RelayToPayerAsync passes the app's calls to the payer when the app calls the payer through the gateway.
/// </summary>
public class DtrService
{
    private readonly IDtrLinkRepository _links;
    private readonly IDtrFormRepository _forms;
    private readonly FhirApiClient _fhir;
    private readonly PayerRegistry _payers;
    private readonly PayerClient _payerClient;
    private readonly ILogger<DtrService> _logger;

    /// <summary>Created by dependency injection.</summary>
    public DtrService(IDtrLinkRepository links, IDtrFormRepository forms, FhirApiClient fhir, PayerRegistry payers,
        PayerClient payerClient, ILogger<DtrService> logger)
    {
        _links = links;
        _forms = forms;
        _fhir = fhir;
        _payers = payers;
        _payerClient = payerClient;
        _logger = logger;
    }

    // ================================================================== 1. get the payer's forms

    /// <summary>Gets the payer's questionnaire(s) for the orders and returns them as simple forms.</summary>
    public async Task<List<DtrForm>> CreateFormsAsync(DtrFormRequest request, CancellationToken ct)
    {
        List<string> problems = new List<string>();
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

        // CLIENT-EHR: the EHR sends the patient's active insurance; the orders carry no insurance.
        if (string.IsNullOrWhiteSpace(request.CoverageId))
        {
            problems.Add("coverageId is required.");
        }

        if (problems.Count > 0)
        {
            throw new ApiException(400, "The request is not valid.", problems);
        }

        PayerSettings payer = await _payers.GetRequiredAsync(request.PayerName, 400, ct);

        // ---- orders and coverage from the FHIR API
        List<string> orderIds = TextHelper.Distinct(request.OrderIds);
        List<JsonObject> orders = await _fhir.ReadOrdersAsync(orderIds, ct);
        OrderHelper.CheckOrders(orderIds, orders, request.PatientId);

        // CLIENT-EHR: the insurance is the one the EHR sends (coverageId), read by its id at the FHIR API.
        // REQUIRED (not optional): the coverage input of $questionnaire-package is 1..1, so an active insurance is needed.
        string coverageId = request.CoverageId.Trim();
        Coverage? coverage = await _fhir.ReadCoverageAsync(coverageId, ct);
        string coverageProblem = OrderHelper.CoverageProblem(coverage, coverageId, request.PatientId);
        if (coverage == null || coverageProblem.Length > 0)
        {
            throw new ApiException(422, coverageProblem);
        }

        // ---- $questionnaire-package input
        Parameters input = new Parameters();
        input.Meta = new Meta();
        input.Meta.Profile.Add(DtrCodes.InputParametersProfile);

        JsonObject? sentCoverage = FhirJson.ToNode(coverage) as JsonObject;

        ParametersParameter coverageParameter = new ParametersParameter();
        coverageParameter.Name = DtrCodes.ParamCoverage;
        coverageParameter.Resource = sentCoverage;
        input.Parameter.Add(coverageParameter);

        foreach (JsonObject order in orders)
        {
            ParametersParameter orderParameter = new ParametersParameter();
            orderParameter.Name = DtrCodes.ParamOrder;
            orderParameter.Resource = FhirJson.Copy(order);
            input.Parameter.Add(orderParameter);
        }
        foreach (string url in TextHelper.Distinct(request.QuestionnaireUrls))
        {
            ParametersParameter questionnaireParameter = new ParametersParameter();
            questionnaireParameter.Name = DtrCodes.ParamQuestionnaire;
            questionnaireParameter.ValueCanonical = url;
            input.Parameter.Add(questionnaireParameter);
        }
        if (!string.IsNullOrWhiteSpace(request.CoverageAssertionId))
        {
            ParametersParameter contextParameter = new ParametersParameter();
            contextParameter.Name = DtrCodes.ParamContext;
            contextParameter.ValueString = request.CoverageAssertionId;
            input.Parameter.Add(contextParameter);
        }

        // ---- call the payer
        PayerCallResult result = await _payerClient.PostFhirAsync(payer, DtrCodes.QuestionnairePackage, input, ct);
        if (!result.IsSuccess())
        {
            int status = result.StatusCode >= 400 && result.StatusCode < 500 ? 422 : 502;
            throw new ApiException(status, payer.GetDisplayName() + " did not return the questionnaire package.", result.Messages());
        }
        List<Questionnaire> questionnaires = ReadQuestionnaires(result.Body);
        if (questionnaires.Count == 0)
        {
            throw new ApiException(422, payer.GetDisplayName() + " returned no questionnaire for these orders.");
        }

        // ---- one form per questionnaire
        List<DtrForm> forms = new List<DtrForm>();
        foreach (Questionnaire questionnaire in questionnaires)
        {
            DtrFormRecord record = new DtrFormRecord();
            record.FormId = "frm-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            record.PayerName = payer.PayerName;
            record.PatientId = request.PatientId;
            record.CoverageId = coverage.Id;
            record.OrderIds = orderIds;
            record.QuestionnaireUrl = questionnaire.GetCanonical() ?? ("Questionnaire/" + questionnaire.Id);
            record.QuestionnaireJson = FhirJson.Serialize(questionnaire);
            record.Status = DtrCodes.StatusNew;
            record.Adaptive = IsAdaptive(questionnaire);

            Questionnaire shown = questionnaire;
            if (record.Adaptive)
            {
                // adaptive: the payer sends the questions itself - ask for the first ones now
                await AskPayerForQuestionsAsync(payer, record, questionnaire, new List<QuestionnaireResponseItem>(), ct);
                shown = ReadQuestionnaire(record);
            }
            await _forms.SaveAsync(record, ct);

            forms.Add(QuestionnaireReader.ToForm(record, shown));
        }

        _logger.LogInformation("DTR: {Count} form(s) from {Payer} for patient {PatientId}", forms.Count, payer.PayerName, request.PatientId);
        return forms;
    }

    /// <summary>A form with its questions and the answers saved so far.</summary>
    public async Task<DtrForm> GetFormAsync(string formId, CancellationToken ct)
    {
        DtrFormRecord record = await GetRecordAsync(formId, ct);
        return QuestionnaireReader.ToForm(record, ReadQuestionnaire(record));
    }

    // ================================================================== 3. save the answers

    /// <summary>
    /// Merges the new answers into the form (an answer with no values removes that answer), builds the
    /// QuestionnaireResponse and saves it at the FHIR API.
    ///   wrong value            -> 400, nothing saved
    ///   complete + missing     -> 422 with the list of missing required questions, nothing saved
    ///   otherwise              -> saved as completed / in-progress
    /// Adaptive forms: the answers are first sent to the payer ($next-question). When the payer adds questions,
    /// the form stays in-progress (newQuestions > 0): read the form again, answer them and save again.
    /// It can only be completed once the payer says there are no more questions.
    /// </summary>
    public async Task<DtrAnswersResult> SaveAnswersAsync(string formId, DtrAnswersRequest request, CancellationToken ct)
    {
        DtrFormRecord record = await GetRecordAsync(formId, ct);
        Questionnaire questionnaire = ReadQuestionnaire(record);

        // ---- merge: latest value per linkId wins; empty values remove the answer
        List<DtrAnswerInput> merged = MergeAnswers(record.Answers, request.Answers);

        // ---- build and check the answers against the questions asked so far
        QuestionnaireResponseBuilder builder = new QuestionnaireResponseBuilder(questionnaire, merged);
        List<QuestionnaireResponseItem> items = builder.BuildItems();
        if (builder.Errors.Count > 0)
        {
            throw new ApiException(400, "Some answers are not valid. Nothing was saved.", builder.Errors);
        }

        // ---- adaptive: send the answers to the payer, which may add the next questions
        int newQuestions = 0;
        if (record.Adaptive && !record.PayerFinished)
        {
            PayerSettings payer = await _payers.GetRequiredAsync(record.PayerName, 422, ct);
            newQuestions = await AskPayerForQuestionsAsync(payer, record, questionnaire, items, ct);
            if (newQuestions > 0)
            {
                // check again with the new questions (they are unanswered, so they show as missing)
                questionnaire = ReadQuestionnaire(record);
                builder = new QuestionnaireResponseBuilder(questionnaire, merged);
                items = builder.BuildItems();
            }
        }

        bool moreToCome = record.Adaptive && (!record.PayerFinished || newQuestions > 0);
        bool complete = request.Complete && !moreToCome;
        if (complete && builder.MissingRequired.Count > 0)
        {
            throw new ApiException(422, "The form cannot be completed: required questions are not answered. Nothing was saved.", builder.MissingRequired);
        }

        // ---- the FHIR QuestionnaireResponse, saved at the FHIR API
        QuestionnaireResponse response = await BuildResponseAsync(record, questionnaire, items,
            complete ? DtrCodes.StatusCompleted : DtrCodes.StatusInProgress, request.AuthorPractitionerId, ct);
        JsonObject saved = await _fhir.SaveQuestionnaireResponseAsync(FhirJson.ToNode(response), ct);
        string savedId = FhirJson.Id(saved) ?? response.Id ?? ("qr-" + record.FormId);

        record.Answers = merged;
        record.Status = response.Status ?? DtrCodes.StatusInProgress;
        record.QuestionnaireResponseId = savedId;
        await _forms.SaveAsync(record, ct);

        DtrAnswersResult result = new DtrAnswersResult();
        result.FormId = record.FormId;
        result.Status = record.Status;
        result.QuestionnaireResponseId = savedId;
        result.AnsweredCount = builder.AnsweredCount;
        result.MissingRequired = builder.MissingRequired;
        result.Warnings = builder.Warnings;
        result.Adaptive = record.Adaptive;
        result.NewQuestions = newQuestions;
        result.MoreQuestionsExpected = moreToCome;
        if (request.Complete && moreToCome)
        {
            result.Warnings.Add("The payer has more questions (" + newQuestions + " new). Answer them and submit again.");
        }

        _logger.LogInformation("DTR form {FormId}: {Status}, {Count} answer(s), {New} new question(s), saved as {QrId}",
            formId, record.Status, builder.AnsweredCount, newQuestions, savedId);
        return result;
    }

    // ================================================================== adaptive forms ($next-question)

    /// <summary>
    /// Sends the current answers to the payer's Questionnaire/$next-question and keeps the questions it adds.
    /// The payer may add one question per call, so it is called again until it adds nothing (it waits for an
    /// answer) or says it is finished (status completed). Updates the record; returns how many questions were added.
    /// </summary>
    private async Task<int> AskPayerForQuestionsAsync(PayerSettings payer, DtrFormRecord record, Questionnaire questionnaire,
        List<QuestionnaireResponseItem> items, CancellationToken ct)
    {
        int added = 0;
        Questionnaire current = questionnaire;
        for (int round = 0; round < MaxAdaptiveRounds; round++)
        {
            QuestionnaireResponse outgoing = await BuildResponseAsync(record, current, items, DtrCodes.StatusInProgress, null, ct);

            Parameters input = new Parameters();
            ParametersParameter inParameter = new ParametersParameter();
            inParameter.Name = DtrCodes.ParamQuestionnaireResponse;
            inParameter.Resource = FhirJson.ToNode(outgoing);
            input.Parameter.Add(inParameter);

            PayerCallResult result = await _payerClient.PostFhirAsync(payer, DtrCodes.NextQuestion, input, ct);
            if (!result.IsSuccess())
            {
                int status = result.StatusCode >= 400 && result.StatusCode < 500 ? 422 : 502;
                throw new ApiException(status, payer.GetDisplayName() + " did not return the next question.", result.Messages());
            }
            JsonObject? returned = ReturnedResponse(result.Body);
            Questionnaire? next = ContainedQuestionnaire(returned);
            if (returned == null || next == null)
            {
                throw new ApiException(502, payer.GetDisplayName() + " returned no QuestionnaireResponse with a contained Questionnaire from $next-question.");
            }

            int newCount = next.Item.Count - current.Item.Count;
            current = next;
            record.QuestionnaireJson = FhirJson.Serialize(current);
            record.AdaptiveResponseJson = returned.ToJsonString();

            if (FhirJson.GetString(returned, "status") == DtrCodes.StatusCompleted)
            {
                record.PayerFinished = true;
                break;
            }
            if (newCount <= 0)
            {
                break;   // the payer waits for answers to the questions asked
            }
            added = added + newCount;
        }
        return added;
    }

    /// <summary>At most this many $next-question calls per save (protects against a payer that never stops).</summary>
    private const int MaxAdaptiveRounds = 25;

    /// <summary>True when the Questionnaire is adaptive (sdc-questionnaire-adaptive), or has no questions of its own.</summary>
    private static bool IsAdaptive(Questionnaire questionnaire)
    {
        Extension? adaptive = Extension.Find(questionnaire.Extension, DtrCodes.AdaptiveExtensionUrl);
        if (adaptive == null)
        {
            adaptive = Extension.Find(questionnaire.Extension, DtrCodes.AdaptiveExtensionUrlSdc3);
        }
        if (adaptive != null && adaptive.ValueBoolean != false)
        {
            return true;
        }
        return questionnaire.Item.Count == 0;
    }

    /// <summary>The QuestionnaireResponse in a $next-question answer (bare, or the "return" parameter of a Parameters).</summary>
    private static JsonObject? ReturnedResponse(JsonObject? body)
    {
        string? type = FhirJson.ResourceType(body);
        if (type == "QuestionnaireResponse")
        {
            return body;
        }
        if (type == "Parameters")
        {
            Parameters? parameters = FhirJson.Read<Parameters>(body);
            if (parameters != null)
            {
                foreach (ParametersParameter parameter in parameters.Parameter)
                {
                    if (parameter.Resource != null && FhirJson.ResourceType(parameter.Resource) == "QuestionnaireResponse")
                    {
                        return parameter.Resource;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>The Questionnaire contained in a QuestionnaireResponse, or null.</summary>
    private static Questionnaire? ContainedQuestionnaire(JsonObject? response)
    {
        JsonArray? contained = response == null ? null : response["contained"] as JsonArray;
        if (contained == null)
        {
            return null;
        }
        foreach (JsonNode? node in contained)
        {
            if (FhirJson.ResourceType(node) == "Questionnaire")
            {
                return FhirJson.Read<Questionnaire>(node);
            }
        }
        return null;
    }

    // ================================================================== QuestionnaireResponse

    /// <summary>
    /// The DTR QuestionnaireResponse of a form: answers, subject, author, and qr-context (orders + coverage, used by PAS
    /// to attach the form to the right order). Adaptive forms carry the payer Questionnaire inside (contained).
    /// </summary>
    private async Task<QuestionnaireResponse> BuildResponseAsync(DtrFormRecord record, Questionnaire questionnaire,
        List<QuestionnaireResponseItem> items, string status, string? authorPractitionerId, CancellationToken ct)
    {
        QuestionnaireResponse response = new QuestionnaireResponse();
        response.Id = "qr-" + record.FormId;
        response.Meta = new Meta();
        response.Meta.Profile.Add(DtrCodes.QuestionnaireResponseProfile);
        response.Status = status;
        response.Subject = new ResourceReference("Patient/" + record.PatientId);
        response.Authored = FhirDate.Now();
        if (!string.IsNullOrWhiteSpace(authorPractitionerId))
        {
            response.Author = new ResourceReference("Practitioner/" + authorPractitionerId);
        }
        response.Item = items;

        if (record.Adaptive)
        {
            Questionnaire copy = FhirJson.Read<Questionnaire>(FhirJson.ToNode(questionnaire)) ?? new Questionnaire();
            copy.Id = DtrCodes.ContainedQuestionnaireId;
            response.Contained.Add(ForContained(FhirJson.ToNode(copy)));
            response.Questionnaire = "#" + DtrCodes.ContainedQuestionnaireId;
        }
        else
        {
            response.Questionnaire = record.QuestionnaireUrl;
        }

        List<JsonObject> orders = await _fhir.ReadOrdersAsync(record.OrderIds, ct);
        foreach (JsonObject raw in orders)
        {
            Extension context = new Extension(DtrCodes.QrContextUrl);
            context.ValueReference = new ResourceReference(OrderHelper.ReadOrder(raw).GetReference());
            response.Extension.Add(context);
        }
        if (!string.IsNullOrEmpty(record.CoverageId))
        {
            Extension coverageContext = new Extension(DtrCodes.QrContextUrl);
            coverageContext.ValueReference = new ResourceReference("Coverage/" + record.CoverageId);
            response.Extension.Add(coverageContext);
        }
        return response;
    }

    /// <summary>
    /// Makes a resource fit to be "contained" in another resource. FHIR does not allow a contained resource to have
    /// narrative text, its own contained resources, a version / last-updated stamp or security labels, so those are
    /// removed. Everything else the payer sent (profile, extensions, questions) stays.
    /// </summary>
    private static JsonObject ForContained(JsonObject resource)
    {
        resource.Remove("text");
        resource.Remove("contained");
        JsonObject? meta = resource["meta"] as JsonObject;
        if (meta != null)
        {
            meta.Remove("versionId");
            meta.Remove("lastUpdated");
            meta.Remove("security");
            if (meta.Count == 0)
            {
                resource.Remove("meta");
            }
        }
        return resource;
    }

    // ================================================================== later: payer SMART DTR apps

    /// <summary>Creates the SMART launch for a DTR link and returns the URL to send the browser to.</summary>
    public async Task<string> LaunchAsync(string linkId, CancellationToken ct)
    {
        DtrLinkRecord? link = await _links.GetAsync(linkId, ct);
        if (link == null || link.ExpiresAt < DateTime.UtcNow)
        {
            throw new ApiException(404, "This documentation link has expired. Sign the order again to get a new link.");
        }
        if (string.IsNullOrEmpty(link.AppUrl))
        {
            throw new ApiException(422, "No DTR app URL is known for this link (PA_PAYER.DTR_APP_LAUNCH_URL, Workflow:DtrAppUrl, or the payer's card).");
        }

        // the payer must still be configured and enabled
        PayerSettings? payer = await _payers.FindAsync(link.PayerName, ct);
        if (payer == null)
        {
            throw new ApiException(404, "Payer '" + link.PayerName + "' is no longer configured or enabled.");
        }

        SmartLaunchResult launch = await _fhir.CreateSmartLaunchAsync(link.PatientId, link.UserPractitionerId, link.EncounterId,
            link.FhirContext, link.AppContext, ct);

        _logger.LogInformation("DTR launch {LaunchId} for patient {PatientId}: {Payer}",
            launch.LaunchId, link.PatientId, payer.PayerName);
        string separator = link.AppUrl.Contains('?') ? "&" : "?";
        return link.AppUrl + separator + "iss=" + Uri.EscapeDataString(launch.Iss) + "&launch=" + Uri.EscapeDataString(launch.LaunchId);
    }

    /// <summary>
    /// The forms saved for a patient at the FHIR API (by the gateway forms or a payer's SMART DTR app), newest first,
    /// optionally only those of one order. Lets the EHR see that the payer's app finished the questionnaire.
    /// </summary>
    public async Task<List<SavedForm>> GetSavedFormsAsync(string? patientId, string? orderId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(patientId))
        {
            throw new ApiException(400, "patientId is required.");
        }
        Bundle bundle = await _fhir.SearchAsync("QuestionnaireResponse?patient=" + Uri.EscapeDataString(patientId.Trim()), ct);
        List<SavedForm> result = new List<SavedForm>();
        foreach (JsonObject resource in bundle.ResourcesOfType("QuestionnaireResponse"))
        {
            SavedForm form = new SavedForm();
            form.QuestionnaireResponseId = FhirJson.Id(resource) ?? string.Empty;
            form.Questionnaire = FhirJson.GetString(resource, "questionnaire");
            form.Status = FhirJson.GetString(resource, "status") ?? string.Empty;
            form.Authored = FhirJson.GetString(resource, "authored");

            // a form can be for several orders: it is listed when the order asked for is one of them
            List<string> formOrderIds = PasBundleBuilder.FormOrderIds(resource);
            if (string.IsNullOrWhiteSpace(orderId))
            {
                if (formOrderIds.Count > 0)
                {
                    form.OrderId = formOrderIds[0];
                }
                result.Add(form);
            }
            else if (formOrderIds.Contains(orderId.Trim()))
            {
                form.OrderId = orderId.Trim();
                result.Add(form);
            }
        }
        return result;
    }

    /// <summary>
    /// Passes a payer DTR app request ($questionnaire-package or $next-question) to the payer unchanged.
    /// The app only knows the gateway, so the gateway adds the payer's URL.
    /// </summary>
    public async Task<PayerCallResult> RelayToPayerAsync(string payerName, string operationPath, JsonObject body, CancellationToken ct)
    {
        PayerSettings payer = await _payers.GetRequiredAsync(payerName, 404, ct);
        return await _payerClient.PostFhirAsync(payer, operationPath, body, ct);
    }

    // ================================================================== helpers

    /// <summary>The stored form, or 404.</summary>
    private async Task<DtrFormRecord> GetRecordAsync(string formId, CancellationToken ct)
    {
        DtrFormRecord? record = await _forms.GetAsync(formId, ct);
        if (record == null)
        {
            throw new ApiException(404, "Form " + formId + " was not found. Create it with POST /api/v1/dtr/forms.");
        }
        return record;
    }

    /// <summary>The stored Questionnaire of a form.</summary>
    private static Questionnaire ReadQuestionnaire(DtrFormRecord record)
    {
        Questionnaire? questionnaire = FhirJson.Deserialize<Questionnaire>(record.QuestionnaireJson);
        if (questionnaire == null)
        {
            throw new ApiException(500, "The questionnaire of form " + record.FormId + " could not be read.");
        }
        return questionnaire;
    }

    /// <summary>
    /// Every Questionnaire in the payer's answer. Normally a Parameters with one PackageBundle per questionnaire;
    /// a plain Bundle or a single Questionnaire is accepted too.
    /// </summary>
    private static List<Questionnaire> ReadQuestionnaires(JsonObject? body)
    {
        List<Questionnaire> questionnaires = new List<Questionnaire>();
        string? type = FhirJson.ResourceType(body);

        if (type == "Questionnaire")
        {
            AddQuestionnaire(body, questionnaires);
        }
        else if (type == "Bundle")
        {
            AddFromBundle(body, questionnaires);
        }
        else if (type == "Parameters")
        {
            Parameters? parameters = FhirJson.Read<Parameters>(body);
            if (parameters != null)
            {
                foreach (ParametersParameter parameter in parameters.Parameter)
                {
                    if (parameter.Name == DtrCodes.ParamPackageBundle)
                    {
                        AddFromBundle(parameter.Resource, questionnaires);
                    }
                }
            }
        }
        return questionnaires;
    }

    /// <summary>Adds the Questionnaires of a Bundle.</summary>
    private static void AddFromBundle(JsonObject? bundleJson, List<Questionnaire> questionnaires)
    {
        Bundle? bundle = FhirJson.Read<Bundle>(bundleJson);
        if (bundle == null)
        {
            return;
        }
        foreach (JsonObject raw in bundle.ResourcesOfType("Questionnaire"))
        {
            AddQuestionnaire(raw, questionnaires);
        }
    }

    /// <summary>Adds one Questionnaire (skips one that is already in the list).</summary>
    private static void AddQuestionnaire(JsonObject? raw, List<Questionnaire> questionnaires)
    {
        Questionnaire? questionnaire = FhirJson.Read<Questionnaire>(raw);
        if (questionnaire == null)
        {
            return;
        }
        foreach (Questionnaire existing in questionnaires)
        {
            if (existing.Url != null && existing.Url == questionnaire.Url && existing.Version == questionnaire.Version)
            {
                return;
            }
        }
        questionnaires.Add(questionnaire);
    }

    /// <summary>Old answers + new answers: a new answer replaces the old one of the same linkId; no values = remove.</summary>
    private static List<DtrAnswerInput> MergeAnswers(List<DtrAnswerInput> previous, List<DtrAnswerInput> latest)
    {
        Dictionary<string, DtrAnswerInput> byLinkId = new Dictionary<string, DtrAnswerInput>();
        List<string> order = new List<string>();

        foreach (DtrAnswerInput answer in previous)
        {
            if (!byLinkId.ContainsKey(answer.LinkId))
            {
                order.Add(answer.LinkId);
            }
            byLinkId[answer.LinkId] = answer;
        }
        foreach (DtrAnswerInput? answer in latest)
        {
            if (answer == null || string.IsNullOrWhiteSpace(answer.LinkId) || answer.Values == null)
            {
                continue;   // an empty entry in the list
            }
            if (!byLinkId.ContainsKey(answer.LinkId))
            {
                order.Add(answer.LinkId);
            }
            byLinkId[answer.LinkId] = answer;
        }

        List<DtrAnswerInput> merged = new List<DtrAnswerInput>();
        foreach (string linkId in order)
        {
            DtrAnswerInput answer = byLinkId[linkId];
            bool hasValue = false;
            foreach (string value in answer.Values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    hasValue = true;
                }
            }
            if (hasValue)
            {
                merged.Add(answer);
            }
        }
        return merged;
    }
}
