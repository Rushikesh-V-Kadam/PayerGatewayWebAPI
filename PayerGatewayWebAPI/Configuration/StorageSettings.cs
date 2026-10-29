namespace PayerGatewayWebAPI.Configuration;

/// <summary>Where gateway data and the payer configuration are stored (appsettings "Storage").</summary>
public class StorageSettings
{
    /// <summary>Name of the appsettings section.</summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// Gateway data (decisions, forms, prior authorization requests ...):
    /// "EhrApi"   = the EHR's Oracle database, through the EHR data API (appsettings "Ehr").
    /// "InMemory" = local testing without the EHR: data is lost on restart.
    /// The payer configuration does NOT follow this setting: it is always read from the EHR data API
    /// (GET payers/{piInsurancePlanId}, table PA_PAYER).
    /// </summary>
    public string Provider { get; set; } = "InMemory";

    /// <summary>How long one payer's configuration is kept before it is read again, in minutes. 0 = read it for every request.</summary>
    public int PayerCacheMinutes { get; set; } = 5;

    /// <summary>True when the EHR data API is selected for gateway data.</summary>
    public bool IsEhrApi()
    {
        return string.Equals(Provider, "EhrApi", StringComparison.OrdinalIgnoreCase);
    }
}
