using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Constants;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Helpers;

namespace PayerGatewayWebAPI.Clients;

/// <summary>
/// Calls the EHR data API: the Web API the EHR team provides so the gateway can read and save its rows in the
/// EHR's own Oracle database (tables PA_*, see Database/01_create_tables.sql and docs/EHR-Data-API.md).
/// The gateway has no database connection of its own; every repository in Repositories/Database (EhrApi*Repository.cs) uses this client.
///   GET {Ehr:BaseUrl}/{Ehr:DataPath}/{path}    one row (404 = no such row) or a list of rows
///   PUT {Ehr:BaseUrl}/{Ehr:DataPath}/{path}    insert or update one row (tried up to 3 times)
/// Rows are plain JSON with camelCase names; one JSON property = one table column.
/// Any failure becomes EhrException (returned to the caller as 503).
/// </summary>
public class EhrDataClient
{
    /// <summary>
    /// How often a save is tried. Saving the same row twice is harmless (PUT = insert or update), and a save must
    /// not be lost: it often follows a payer call that cannot be repeated (e.g. an accepted prior auth request).
    /// </summary>
    private const int SaveAttempts = 3;

    private readonly IHttpClientFactory _httpFactory;
    private readonly EhrSettings _settings;
    private readonly ILogger<EhrDataClient> _logger;

    /// <summary>Created by dependency injection.</summary>
    public EhrDataClient(IHttpClientFactory httpFactory, IOptions<EhrSettings> settings, ILogger<EhrDataClient> logger)
    {
        _httpFactory = httpFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>Reads one row. Returns null when the EHR answers 404 (the row does not exist).</summary>
    public async Task<T?> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        string? text = await SendAsync(HttpMethod.Get, path, null, true, ct);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        return Read<T>(path, text);
    }

    /// <summary>Reads a list of rows (a JSON array). An empty answer is an empty list.</summary>
    public async Task<List<T>> GetListAsync<T>(string path, CancellationToken ct)
    {
        string? text = await SendAsync(HttpMethod.Get, path, null, false, ct);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<T>();
        }
        List<T>? rows = Read<List<T>>(path, text);
        if (rows == null)
        {
            return new List<T>();
        }
        return rows;
    }

    /// <summary>Inserts the row, or updates it when the key in the path already exists.</summary>
    public async Task PutAsync<T>(string path, T row, CancellationToken ct)
    {
        string json = ApiJson.Write(row);
        for (int attempt = 1; attempt <= SaveAttempts; attempt++)
        {
            try
            {
                await SendAsync(HttpMethod.Put, path, json, false, ct);
                return;
            }
            catch (EhrException ex)
            {
                if (attempt == SaveAttempts)
                {
                    throw;   // still failing: the caller gets 503
                }
                _logger.LogWarning(ex, "Save of {Path} failed (attempt {Attempt} of {Attempts}); trying again", path, attempt, SaveAttempts);
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            }
        }
    }

    /// <summary>Makes an id safe to use inside a URL path or query string.</summary>
    public static string Esc(string value)
    {
        return Uri.EscapeDataString(value);
    }

    // ================================================================== plumbing

    /// <summary>
    /// Sends one call with the configured timeout and returns the response body.
    /// Returns null for 404 when <paramref name="notFoundIsNull"/> is true; every other problem is an EhrException.
    /// </summary>
    private async Task<string?> SendAsync(HttpMethod method, string path, string? json, bool notFoundIsNull, CancellationToken ct)
    {
        string url = _settings.GetDataUrl() + "/" + path;
        string label = "EHR data API " + method.Method + " " + path;

        using (HttpRequestMessage request = new HttpRequestMessage(method, url))
        using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            if (json != null)
            {
                request.Content = new StringContent(json, Encoding.UTF8, ApiHeaders.Json);
            }
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds)));

            try
            {
                HttpClient http = _httpFactory.CreateClient();
                using (HttpResponseMessage response = await http.SendAsync(request, timeout.Token))
                {
                    string text = await response.Content.ReadAsStringAsync(timeout.Token);
                    int status = (int)response.StatusCode;
                    _logger.LogDebug("{Label} -> {Status}", label, status);

                    if (status == 404 && notFoundIsNull)
                    {
                        return null;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new EhrException(label + " returned HTTP " + status + ": " + TextHelper.Shorten(text, 300));
                    }
                    return text;
                }
            }
            catch (OperationCanceledException ex)
            {
                if (ct.IsCancellationRequested)
                {
                    throw;   // the caller is gone
                }
                throw new EhrException(label + " did not answer within " + _settings.TimeoutSeconds + " seconds.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new EhrException(label + " failed: " + ex.Message, ex);
            }
        }
    }

    /// <summary>JSON text -> object; a body that is not the expected JSON becomes EhrException.</summary>
    private static T? Read<T>(string path, string text)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, ApiJson.Options);
        }
        catch (JsonException ex)
        {
            throw new EhrException("EHR data API GET " + path + " returned a body that could not be read: " + ex.Message, ex);
        }
    }
}
