namespace PayerGatewayWebAPI.Configuration;

/// <summary>Where gateway data and the payer list are stored (appsettings "Storage").</summary>
public class StorageSettings
{
    /// <summary>Name of the appsettings section.</summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// "EhrApi"   = the EHR's Oracle database, through the EHR data API (appsettings "Ehr"). Payers come from table PA_PAYER.
    /// "InMemory" = local testing without the EHR: data is lost on restart, payers come from appsettings "Payers".
    /// </summary>
    public string Provider { get; set; } = "InMemory";

    /// <summary>How long the payer list is kept before it is read again, in minutes (a new payer is also found right away).</summary>
    public int PayerCacheMinutes { get; set; } = 5;

    /// <summary>True when the EHR data API is selected.</summary>
    public bool IsEhrApi()
    {
        return string.Equals(Provider, "EhrApi", StringComparison.OrdinalIgnoreCase);
    }
}
