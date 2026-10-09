using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Constants;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Models.CdsHooks;
using PayerGatewayWebAPI.Models.Fhir;

namespace PayerGatewayWebAPI.Services.Payers;

/// <summary>
/// Calls any payer's Da Vinci APIs. The same code serves every payer; only the PayerSettings differ
/// (base URL, paths, timeouts). No authentication is added to payer calls. Endpoints called:
///   CRD   GET  {cds}                  discovery          POST {cds}/{serviceId}         order-sign hook call
///   DTR   POST {fhir}/Questionnaire/$questionnaire-package, Questionnaire/$next-question
///   PAS   POST {fhir}/Claim/$submit, Claim/$inquire, Subscription
///   CDex  GET  {fhir}/Task?owner=...  POST {fhir}/$submit-attachment
/// </summary>
public class PayerClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<PayerClient> _logger;

    /// <summary>Created by dependency injection.</summary>
    public PayerClient(IHttpClientFactory httpFactory, ILogger<PayerClient> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    // ================================================================== CRD (CDS Hooks, plain JSON)

    /// <summary>GET {cds}: the payer's CDS services and prefetch templates.</summary>
    /// <param name="payer">The payer to ask.</param>
    /// <param name="timeoutSeconds">
    /// Seconds to wait: the short CRD timeout when a clinician is waiting (order-sign), the normal timeout otherwise.
    /// </param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    public async Task<CdsDiscoveryResponse> DiscoverAsync(PayerSettings payer, int timeoutSeconds, CancellationToken ct)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, payer.GetCdsServicesUrl());
        string text = await SendForTextAsync(payer, request, timeoutSeconds, ct);
        CdsDiscoveryResponse? discovery = ReadJson<CdsDiscoveryResponse>(payer, text);
        if (discovery == null)
        {
            return new CdsDiscoveryResponse();
        }
        // "services": null is allowed JSON; the rest of the code expects a list
        if (discovery.Services == null)
        {
            discovery.Services = new List<CdsServiceDefinition>();
        }
        return discovery;
    }

    /// <summary>POST {cds}/{serviceId}: the order-sign hook call. Uses the short CRD timeout.</summary>
    public async Task<CdsHookResponse> CallHookAsync(PayerSettings payer, string serviceId, CdsHookRequest hookRequest, CancellationToken ct)
    {
        string url = payer.GetCdsServicesUrl() + "/" + serviceId;
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(FhirJson.Serialize(hookRequest), Encoding.UTF8, ApiHeaders.Json);

        string text = await SendForTextAsync(payer, request, payer.CrdTimeoutSeconds, ct);
        CdsHookResponse? response = ReadJson<CdsHookResponse>(payer, text);
        if (response == null)
        {
            return new CdsHookResponse();
        }
        // "cards": null, "systemActions": null and "links": null are allowed JSON (some CDS services write them
        // that way); the rest of the code expects lists, so each is replaced by an empty list
        if (response.Cards == null)
        {
            response.Cards = new List<CdsCard>();
        }
        if (response.SystemActions == null)
        {
            response.SystemActions = new List<CdsAction>();
        }
        List<CdsCard> cards = new List<CdsCard>();
        foreach (CdsCard? card in response.Cards)
        {
            if (card == null)
            {
                continue;
            }
            if (card.Links == null)
            {
                card.Links = new List<CdsLink>();
            }
            cards.Add(card);
        }
        response.Cards = cards;
        return response;
    }

    // ================================================================== FHIR operations (DTR, PAS, CDex)

    /// <summary>POST a FHIR body to {fhir}/{path}. The caller decides what a non-2xx result means.</summary>
    public async Task<PayerCallResult> PostFhirAsync(PayerSettings payer, string path, object body, CancellationToken ct)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, payer.GetFhirBaseUrl() + "/" + path);
        string json;
        JsonNode? node = body as JsonNode;
        if (node != null)
        {
            json = node.ToJsonString();
        }
        else
        {
            json = FhirJson.Serialize(body);
        }
        request.Content = new StringContent(json, Encoding.UTF8, ApiHeaders.FhirJson);
        return await SendFhirAsync(payer, request, ct);
    }

    /// <summary>GET {fhir}/{pathAndQuery}.</summary>
    public async Task<PayerCallResult> GetFhirAsync(PayerSettings payer, string pathAndQuery, CancellationToken ct)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, payer.GetFhirBaseUrl() + "/" + pathAndQuery);
        return await SendFhirAsync(payer, request, ct);
    }

    // ================================================================== plumbing

    /// <summary>Adds Accept, sends, and parses the JSON body. No authentication is added (handled outside this code).</summary>
    private async Task<PayerCallResult> SendFhirAsync(PayerSettings payer, HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ApiHeaders.FhirJson));

        PayerCallResult result = new PayerCallResult();
        using (HttpResponseMessage response = await SendAsync(payer, request, payer.TimeoutSeconds, ct))
        {
            string text = await response.Content.ReadAsStringAsync(ct);
            result.StatusCode = (int)response.StatusCode;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    result.Body = JsonNode.Parse(text) as JsonObject;
                }
                catch (JsonException)
                {
                    _logger.LogWarning("Payer {Payer} returned a body that is not JSON", payer.PayerName);
                }
            }
        }

        if (FhirJson.ResourceType(result.Body) == "OperationOutcome")
        {
            try
            {
                result.Outcome = FhirJson.Read<OperationOutcome>(result.Body);
            }
            catch (JsonException ex)
            {
                // the caller still gets the status code and the raw body; only the payer's messages are missing
                _logger.LogWarning(ex, "Payer {Payer} returned an OperationOutcome that could not be read", payer.PayerName);
            }
        }
        return result;
    }

    /// <summary>Sends and returns the body text; a non-2xx status becomes PayerException (502).</summary>
    private async Task<string> SendForTextAsync(PayerSettings payer, HttpRequestMessage request, int timeoutSeconds, CancellationToken ct)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ApiHeaders.Json));
        string? url = request.RequestUri == null ? string.Empty : request.RequestUri.ToString();
        using (HttpResponseMessage response = await SendAsync(payer, request, timeoutSeconds, ct))
        {
            string text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new PayerException(payer.GetDisplayName() + " returned HTTP " + (int)response.StatusCode + " for " + url + ": " + TextHelper.Shorten(text, 300), 502);
            }
            return text;
        }
    }

    /// <summary>Sends with a timeout. Timeout -> PayerException 504, unreachable -> 502.</summary>
    private async Task<HttpResponseMessage> SendAsync(PayerSettings payer, HttpRequestMessage request, int timeoutSeconds, CancellationToken ct)
    {
        using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                HttpClient http = _httpFactory.CreateClient();
                HttpResponseMessage response = await http.SendAsync(request, timeout.Token);
                _logger.LogInformation("Payer {Payer} {Method} {Url} -> {Status} in {Ms} ms",
                    payer.PayerName, request.Method, request.RequestUri, (int)response.StatusCode, watch.ElapsedMilliseconds);
                return response;
            }
            catch (OperationCanceledException ex)
            {
                if (ct.IsCancellationRequested)
                {
                    throw;
                }
                throw new PayerException(payer.GetDisplayName() + " did not answer within " + timeoutSeconds + " seconds.", 504, ex);
            }
            catch (HttpRequestException ex)
            {
                throw new PayerException(payer.GetDisplayName() + " could not be reached: " + ex.Message, 502, ex);
            }
        }
    }

    /// <summary>JSON text -> object; a payer answer that is not valid JSON becomes PayerException (502).</summary>
    private static T? ReadJson<T>(PayerSettings payer, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }
        try
        {
            return FhirJson.Deserialize<T>(text);
        }
        catch (JsonException ex)
        {
            throw new PayerException(payer.GetDisplayName() + " returned an answer that could not be read: " + ex.Message, 502, ex);
        }
    }
}
