namespace PayerGatewayWebAPI.Configuration;

/// <summary>
/// How to reach the EHR data API (appsettings "Ehr"). The gateway keeps all its data in the EHR's Oracle database
/// and reads / saves it through this API (see docs/EHR-Data-API.md). Used when Storage:Provider is "EhrApi".
/// </summary>
public class EhrSettings
{
    /// <summary>Name of the appsettings section.</summary>
    public const string SectionName = "Ehr";

    /// <summary>EHR REST base URL (mock EHR: http://localhost:5090).</summary>
    public string BaseUrl { get; set; } = "http://localhost:5090";

    /// <summary>Path of the data API under BaseUrl (default "api/prior-auth-data").</summary>
    public string DataPath { get; set; } = "api/prior-auth-data";


    /// <summary>Timeout of each data API call, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>{BaseUrl}/{DataPath} without a trailing slash.</summary>
    public string GetDataUrl()
    {
        return BaseUrl.TrimEnd('/') + "/" + DataPath.Trim('/');
    }
}
