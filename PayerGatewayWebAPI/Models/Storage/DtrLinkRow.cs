namespace PayerGatewayWebAPI.Models.Storage;

/// <summary>
/// A payer SMART DTR app link exactly as it is sent to / returned by the EHR data API (dtr-links)
/// = one row of table PA_DTR_LINK.
/// </summary>
public class DtrLinkRow
{
    /// <summary>Column LINK_ID (key).</summary>
    public string LinkId { get; set; } = string.Empty;
    /// <summary>Column PAYER_NAME.</summary>
    public string PayerName { get; set; } = string.Empty;
    /// <summary>Column PATIENT_ID.</summary>
    public string PatientId { get; set; } = string.Empty;
    /// <summary>Column ENCOUNTER_ID.</summary>
    public string? EncounterId { get; set; }
    /// <summary>Column USER_PRACTITIONER_ID.</summary>
    public string? UserPractitionerId { get; set; }
    /// <summary>JSON array of references, e.g. ["ServiceRequest/ord-1001","Coverage/cov-1001"].</summary>
    public string? FhirContextJson { get; set; }
    /// <summary>Column APP_CONTEXT: free text from the payer's CRD card, stored unchanged.</summary>
    public string? AppContext { get; set; }
    /// <summary>Column APP_URL: launch URL of the DTR app.</summary>
    public string AppUrl { get; set; } = string.Empty;
    /// <summary>Column EXPIRES_AT (UTC).</summary>
    public DateTime ExpiresAt { get; set; }
}
