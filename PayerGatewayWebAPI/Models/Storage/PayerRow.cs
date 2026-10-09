namespace PayerGatewayWebAPI.Models.Storage;

/// <summary>
/// One payer exactly as the EHR data API returns it (GET payers) = one row of table PA_PAYER.
/// The EHR team maintains these rows; the gateway only reads them and turns each into a PayerSettings.
/// </summary>
public class PayerRow
{
    /// <summary>The name the EHR sends as payerName (primary key).</summary>
    public string PayerName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    /// <summary>false = configured but switched off.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>The payer's own id, sent in PAS and CDex.</summary>
    public string? PayerIdentifier { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string? CdsServicesPath { get; set; }
    public string? FhirPath { get; set; }
    public bool UseSubscriptions { get; set; } = true;
    public string? MemberIdSystem { get; set; }
    public string? PayerIdSystem { get; set; }
    /// <summary>0 or empty = the gateway default.</summary>
    public int? CrdTimeoutSeconds { get; set; }
    /// <summary>0 or empty = the gateway default.</summary>
    public int? TimeoutSeconds { get; set; }
    /// <summary>R4 | R5 (empty = R4).</summary>
    public string? FhirVersion { get; set; }
    /// <summary>Forms | SmartApp (empty = Forms).</summary>
    public string? DtrMode { get; set; }
    /// <summary>Launch URL of the payer's SMART DTR app; empty = the URL on the CRD card.</summary>
    public string? DtrAppLaunchUrl { get; set; }
}
