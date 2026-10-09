namespace PayerGatewayWebAPI.Configuration;

/// <summary>How to reach the FHIR API (appsettings "FhirApi"). All EHR data is read there as FHIR.</summary>
public class FhirApiSettings
{
    /// <summary>Name of the appsettings section.</summary>
    public const string SectionName = "FhirApi";

    /// <summary>
    /// FHIR base URL, e.g. http://localhost:5100/fhir/r4. Also sent to payers as fhirServer, so payers must be able
    /// to reach it.
    /// TODO: the default is the local test address. Set FhirApi:BaseUrl (and BaseUrlR5) in appsettings.json to the
    /// public address of the FHIR API in each environment.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5100/fhir/r4";

    /// <summary>
    /// R5 base URL of the same API, e.g. http://localhost:5100/fhir/r5. Sent to payers with FhirVersion = R5 as fhirServer,
    /// and used to read the R5 copies of the orders / coverage / prefetch data they receive.
    /// Empty = BaseUrl with "/fhir/r4" changed to "/fhir/r5".
    /// </summary>
    public string? BaseUrlR5 { get; set; }

    /// <summary>
    /// Root URL for the /internal endpoints, e.g. http://localhost:5100.
    /// TODO: the default is the local test address. Set FhirApi:InternalBaseUrl in appsettings.json to the internal
    /// address of the FHIR API in each environment.
    /// </summary>
    public string InternalBaseUrl { get; set; } = "http://localhost:5100";

    /// <summary>Timeout of each call, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>BaseUrl without a trailing slash.</summary>
    public string GetBaseUrl()
    {
        return BaseUrl.TrimEnd('/');
    }

    /// <summary>BaseUrlR5 without a trailing slash (or BaseUrl with r4 replaced by r5).</summary>
    public string GetBaseUrlR5()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrlR5))
        {
            return BaseUrlR5.TrimEnd('/');
        }
        string r4 = GetBaseUrl();
        if (r4.EndsWith("/r4", StringComparison.OrdinalIgnoreCase))
        {
            return r4.Substring(0, r4.Length - 3) + "/r5";
        }
        return r4;
    }
}
