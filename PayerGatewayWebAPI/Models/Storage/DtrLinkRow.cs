namespace PayerGatewayWebAPI.Models.Storage;

/// <summary>
/// A payer SMART DTR app link exactly as it is sent to / returned by the EHR data API (dtr-links)
/// = one row of table PA_DTR_LINK.
/// </summary>
public class DtrLinkRow
{
    public string LinkId { get; set; } = string.Empty;
    public string PayerName { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string? EncounterId { get; set; }
    public string? UserPractitionerId { get; set; }
    /// <summary>JSON array of references, e.g. ["ServiceRequest/ord-1001","Coverage/cov-1001"].</summary>
    public string? FhirContextJson { get; set; }
    public string? AppContext { get; set; }
    public string AppUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
