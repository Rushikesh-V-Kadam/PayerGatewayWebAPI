namespace PayerGatewayWebAPI.Configuration;

/// <summary>How to reach the FHIR API (appsettings "FhirApi"). All EHR data is read there as FHIR.</summary>
public class FhirApiSettings
{
    /// <summary>Name of the appsettings section.</summary>
    public const string SectionName = "FhirApi";

    /// <summary>
    /// FHIR base URL, e.g. http://localhost:5100/fhir/r4. Also sent to payers as fhirServer, so payers must be able
    /// to reach it.
    /// TODO: the default is the local test address. Set FhirApi:BaseUrl in appsettings.json to the
    /// public address of the FHIR API in each environment.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5100/fhir/r4";

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
}
