using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Constants;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Models.Fhir;

namespace PayerGatewayWebAPI.Clients;

/// <summary>
/// Talks to the FHIR API (the gateway never calls the EHR's read APIs itself).
///   GET  {fhir}/{type}/{id}, {fhir}/{type}?...     Patient, Coverage, orders, Organization, documents, forms
///   PUT  {fhir}/QuestionnaireResponse/{id}          saves a DTR form built by the gateway
///   POST {root}/internal/access-tokens              fhirAuthorization token for the payer (CRD)
///   POST {root}/internal/smart-launches             SMART launch for a payer DTR app
/// </summary>
public class FhirApiClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly FhirApiSettings _settings;

    /// <summary>Created by dependency injection.</summary>
    public FhirApiClient(IHttpClientFactory httpFactory, IOptions<FhirApiSettings> settings)
    {
        _httpFactory = httpFactory;
        _settings = settings.Value;
    }

    /// <summary>FHIR base URL of the FHIR API (also sent to payers as fhirServer).</summary>
    public string GetBaseUrl()
    {
        return _settings.GetBaseUrl();
    }

    // ================================================================== reads

    /// <summary>GET {fhir}/{type}/{id}. Null when the resource does not exist.</summary>
    public async Task<JsonObject?> ReadAsync(string resourceType, string id, CancellationToken ct)
    {
        string url = GetBaseUrl() + "/" + resourceType + "/" + Uri.EscapeDataString(id);
        JsonNode? node = await GetJsonAsync(url, ct);
        return node as JsonObject;
    }

    /// <summary>GET {fhir}/{query}, e.g. "Coverage?patient=pat-1&amp;status=active". Returns the searchset Bundle.</summary>
    public async Task<Bundle> SearchAsync(string query, CancellationToken ct)
    {
        string url = GetBaseUrl() + "/" + query.TrimStart('/');
        JsonNode? node = await GetJsonAsync(url, ct);
        Bundle? bundle = FhirJson.Read<Bundle>(node);
        if (bundle == null)
        {
            return new Bundle();
        }
        return bundle;
    }

    /// <summary>Runs a filled-in CRD prefetch query and returns whatever came back (a resource or a Bundle).</summary>
    public async Task<JsonNode?> PrefetchAsync(string query, CancellationToken ct)
    {
        string url = GetBaseUrl() + "/" + query.TrimStart('/');
        return await GetJsonAsync(url, ct);
    }

    /// <summary>
    /// GET {fhir}/Coverage/{id}: the insurance the EHR sent in the request (coverageId). Null when it does not exist.
    /// The EHR's coverage id is unique in the EHR, so the insurance is read by its id alone.
    /// </summary>
    // CLIENT-EHR: the EHR sends the patient's active insurance (coverageId); the orders carry no insurance.
    public async Task<Coverage?> ReadCoverageAsync(string coverageId, CancellationToken ct)
    {
        JsonObject? raw = await ReadAsync("Coverage", coverageId, ct);
        if (raw == null)
        {
            return null;
        }
        return FhirJson.Read<Coverage>(raw);
    }

    /// <summary>
    /// The patient's active coverages (GET {fhir}/Coverage?patient={id}&amp;status=active).
    /// NOT USED in the current flow: the EHR sends the active insurance (coverageId) and the gateway reads it with
    /// ReadCoverageAsync. Kept for later, e.g. a request without coverageId or a check of the patient's other insurances.
    /// </summary>
    public async Task<List<Coverage>> GetActiveCoveragesAsync(string patientId, CancellationToken ct)
    {
        Bundle bundle = await SearchAsync("Coverage?patient=" + Uri.EscapeDataString(patientId) + "&status=active", ct);
        List<Coverage> coverages = new List<Coverage>();
        foreach (JsonObject raw in bundle.ResourcesOfType("Coverage"))
        {
            Coverage? coverage = FhirJson.Read<Coverage>(raw);
            if (coverage != null)
            {
                coverages.Add(coverage);
            }
        }
        return coverages;
    }

    /// <summary>
    /// Reads orders by EHR order id. An order is a ServiceRequest (dialysis service) or a MedicationRequest
    /// (medication), so each of the two types is searched with _id=id1,id2. Orders that do not exist are simply
    /// missing from the result. DeviceRequest is not read: the EHR has no device orders.
    /// </summary>
    public async Task<List<JsonObject>> ReadOrdersAsync(List<string> orderIds, CancellationToken ct)
    {
        List<JsonObject> orders = new List<JsonObject>();
        List<string> escaped = new List<string>();
        foreach (string orderId in TextHelper.Distinct(orderIds))
        {
            escaped.Add(Uri.EscapeDataString(orderId));
        }
        if (escaped.Count == 0)
        {
            return orders;
        }

        string ids = string.Join(",", escaped);
        foreach (string orderType in OrderResource.SupportedTypes)
        {
            Bundle bundle = await SearchAsync(orderType + "?_id=" + ids, ct);
            orders.AddRange(bundle.ResourcesOfType(orderType));
        }
        return orders;
    }

    /// <summary>
    /// Reads a DocumentReference and embeds the file (attachment.data) by downloading its Binary,
    /// because payers must receive the content itself (PAS / CDex), not a link to our server.
    /// The file address may be complete ("http://.../Binary/b1") or relative to the FHIR API ("Binary/b1").
    /// A document without any file (no content, or a file that cannot be downloaded) is an error
    /// (FhirApiException): a document without its content is never sent to a payer.
    /// Null when the DocumentReference itself does not exist.
    /// </summary>
    public async Task<JsonObject?> ReadDocumentWithContentAsync(string documentId, CancellationToken ct)
    {
        JsonObject? document = await ReadAsync("DocumentReference", documentId, ct);
        if (document == null)
        {
            return null;
        }
        JsonArray? contents = document["content"] as JsonArray;
        if (contents == null)
        {
            throw new FhirApiException("Document " + documentId + " has no file (DocumentReference.content is missing), so it cannot be sent to the payer.");
        }

        int files = 0;
        foreach (JsonNode? content in contents)
        {
            JsonObject? attachment = null;
            if (content != null)
            {
                attachment = content["attachment"] as JsonObject;
            }
            if (attachment == null)
            {
                continue;
            }
            if (!string.IsNullOrEmpty(FhirJson.GetString(attachment, "data")))
            {
                files = files + 1;   // the file is already inside the document
                continue;
            }
            string? url = FhirJson.GetString(attachment, "url");
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            // a relative address ("Binary/b1") is an address at the FHIR API
            string fileUrl = url.Trim();
            bool isComplete = fileUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || fileUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            if (!isComplete)
            {
                fileUrl = GetBaseUrl() + "/" + fileUrl.TrimStart('/');
            }

            JsonNode? binary = await GetJsonAsync(fileUrl, ct);
            string? data = FhirJson.GetString(binary, "data");
            if (string.IsNullOrEmpty(data))
            {
                throw new FhirApiException("The file of document " + documentId + " could not be read from " + fileUrl
                    + " (it does not exist or it is empty), so the document cannot be sent to the payer.");
            }
            attachment["data"] = data;
            if (attachment["contentType"] == null)
            {
                attachment["contentType"] = FhirJson.GetString(binary, "contentType");
            }
            attachment.Remove("url");
            files = files + 1;
        }
        if (files == 0)
        {
            throw new FhirApiException("Document " + documentId + " has no file (no attachment with data or url), so it cannot be sent to the payer.");
        }
        return document;
    }

    // ================================================================== DTR forms

    /// <summary>
    /// PUT {fhir}/QuestionnaireResponse/{id}: creates or replaces a DTR form. The FHIR API stores it in the EHR
    /// database (table PA_QUESTIONNAIRE_RESPONSE) and copies it to the EHR chart. Returns the stored resource.
    /// </summary>
    public async Task<JsonObject> SaveQuestionnaireResponseAsync(JsonObject questionnaireResponse, CancellationToken ct)
    {
        string? id = FhirJson.Id(questionnaireResponse);
        if (string.IsNullOrEmpty(id))
        {
            throw new FhirApiException("A QuestionnaireResponse needs an id before it can be saved.");
        }

        string url = GetBaseUrl() + "/QuestionnaireResponse/" + Uri.EscapeDataString(id);
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Content = new StringContent(questionnaireResponse.ToJsonString(), Encoding.UTF8, ApiHeaders.FhirJson);
        request.Headers.Add("Accept", ApiHeaders.FhirJson);

        string text = await SendAsync(request, "PUT QuestionnaireResponse/" + id, false, ct);
        JsonObject? saved = ParseObject(text);
        if (saved == null)
        {
            return questionnaireResponse;
        }
        return saved;
    }

    // ================================================================== /internal

    /// <summary>
    /// Short-lived token that lets the payer read this patient's data during CRD.
    /// Returned unchanged because it already has the CDS Hooks fhirAuthorization shape.
    /// </summary>
    public async Task<JsonObject?> IssueAccessTokenAsync(string payerName, string patientId, int minutes, CancellationToken ct)
    {
        JsonObject body = new JsonObject();
        body["subject"] = payerName;
        body["patientId"] = patientId;
        body["minutes"] = minutes;
        return await PostInternalAsync("internal/access-tokens", body, ct);
    }

    /// <summary>
    /// Creates a SMART launch for a payer DTR app. Returns the launch id and iss (the FHIR R4 base URL).
    /// </summary>
    public async Task<SmartLaunchResult> CreateSmartLaunchAsync(string patientId, string? userPractitionerId,
        string? encounterId, List<string> fhirContext, string? appContext, CancellationToken ct)
    {
        JsonArray contextArray = new JsonArray();
        foreach (string reference in fhirContext)
        {
            contextArray.Add(reference);
        }

        JsonObject body = new JsonObject();
        body["patientId"] = patientId;
        body["userPractitionerId"] = userPractitionerId;
        body["encounterId"] = encounterId;
        body["fhirContext"] = contextArray;
        body["appContext"] = appContext;

        JsonObject? result = await PostInternalAsync("internal/smart-launches", body, ct);
        string? launchId = FhirJson.GetString(result, "launchId");
        string? iss = FhirJson.GetString(result, "iss");
        if (launchId == null || iss == null)
        {
            throw new FhirApiException("The SMART launch response has no launchId / iss.");
        }

        SmartLaunchResult launch = new SmartLaunchResult();
        launch.LaunchId = launchId;
        launch.Iss = iss;
        return launch;
    }

    // ================================================================== plumbing

    /// <summary>GET. 404 -> null; other errors -> FhirApiException.</summary>
    private async Task<JsonNode?> GetJsonAsync(string url, CancellationToken ct)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", ApiHeaders.FhirJson);

        string text = await SendAsync(request, "GET " + url, true, ct);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new FhirApiException("GET " + url + " did not return JSON.", ex);
        }
    }

    /// <summary>POST to {root}/internal/...</summary>
    private async Task<JsonObject?> PostInternalAsync(string path, JsonObject body, CancellationToken ct)
    {
        string url = _settings.InternalBaseUrl.TrimEnd('/') + "/" + path;
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, ApiHeaders.Json);

        string text = await SendAsync(request, "POST " + path, false, ct);
        return ParseObject(text);
    }

    /// <summary>
    /// Sends with the configured timeout and returns the body text.
    /// notFoundIsEmpty = true: a 404 returns "" (resource does not exist). Other errors -> FhirApiException.
    /// </summary>
    private async Task<string> SendAsync(HttpRequestMessage request, string label, bool notFoundIsEmpty, CancellationToken ct)
    {
        using (request)
        using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds)));
            HttpClient http = _httpFactory.CreateClient();
            try
            {
                using (HttpResponseMessage response = await http.SendAsync(request, timeout.Token))
                {
                    if (notFoundIsEmpty && response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return string.Empty;
                    }
                    string text = await response.Content.ReadAsStringAsync(timeout.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new FhirApiException(label + " returned HTTP " + (int)response.StatusCode + ": " + TextHelper.Shorten(text, 300));
                    }
                    return text;
                }
            }
            catch (OperationCanceledException ex)
            {
                if (ct.IsCancellationRequested)
                {
                    throw;
                }
                throw new FhirApiException(label + " did not answer within " + _settings.TimeoutSeconds + " seconds.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new FhirApiException(label + " failed: " + ex.Message, ex);
            }
        }
    }

    /// <summary>Text -> JSON object, or null when empty / not an object.</summary>
    private static JsonObject? ParseObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
