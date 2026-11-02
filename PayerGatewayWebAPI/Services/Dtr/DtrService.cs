using System.Text.Json.Nodes;
using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Models.Fhir;
using PayerGatewayWebAPI.Models.Responses;
using PayerGatewayWebAPI.Repositories.Interfaces;
using PayerGatewayWebAPI.Services.Pas;
using PayerGatewayWebAPI.Services.Payers;

namespace PayerGatewayWebAPI.Services.Dtr;

/// <summary>
/// STEP 2 - DTR (Documentation Templates and Rules) with SMART App Launch. Used when order-sign says callDtr = true.
///
///   1. order-sign returns a message link of type "smart" (/dtr/launch/{linkId}); the EHR opens it in the browser.
///   2. LaunchAsync creates the SMART EHR launch at the FHIR API (FHIR R4) and sends the browser to the payer's
///      SMART DTR app with iss and launch.
///   3. The app gets its launch context and token from the FHIR API, reads the chart, and saves its
///      QuestionnaireResponse at the FHIR API.
///   4. GetSavedFormsAsync lets the EHR see the saved form; PAS attaches completed forms of the orders automatically.
/// The app gets its questionnaires ($questionnaire-package, $next-question) from its own payer, not through the gateway.
/// </summary>
public class DtrService
{
    private readonly IDtrLinkRepository _links;
    private readonly FhirApiClient _fhir;
    private readonly PayerRegistry _payers;
    private readonly ILogger<DtrService> _logger;

    /// <summary>Created by dependency injection.</summary>
    public DtrService(IDtrLinkRepository links, FhirApiClient fhir, PayerRegistry payers, ILogger<DtrService> logger)
    {
        _links = links;
        _fhir = fhir;
        _payers = payers;
        _logger = logger;
    }

    // ================================================================== 1. SMART launch of the payer DTR app

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
        PayerSettings? payer = await _payers.FindAsync(link.InsurancePlanId, ct);
        if (payer == null)
        {
            throw new ApiException(404, "No payer is configured or enabled any more for insurancePlanId " + link.InsurancePlanId + ".");
        }

        SmartLaunchResult launch = await _fhir.CreateSmartLaunchAsync(link.InsurancePlanId, link.PatientId, link.UserPractitionerId, link.EncounterId,
            link.FhirContext, link.AppContext, ct);

        _logger.LogInformation("DTR launch {LaunchId} for patient {PatientId}: {Payer}",
            launch.LaunchId, link.PatientId, payer.PayerName);
        string separator = link.AppUrl.Contains('?') ? "&" : "?";
        return link.AppUrl + separator + "iss=" + Uri.EscapeDataString(launch.Iss) + "&launch=" + Uri.EscapeDataString(launch.LaunchId);
    }

    // ================================================================== 2. forms saved by the SMART app

    /// <summary>
    /// The forms (QuestionnaireResponse) a payer's SMART DTR app saved for a patient at the FHIR API, newest first,
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
}
