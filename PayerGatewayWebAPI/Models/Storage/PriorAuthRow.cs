namespace PayerGatewayWebAPI.Models.Storage;

/// <summary>
/// A prior authorization request exactly as it is sent to / returned by the EHR data API (prior-auths)
/// = one row of table PA_PRIOR_AUTH. Lists, the decision and the FHIR Bundles are sent as JSON text
/// so the EHR can store them unchanged in CLOB columns.
/// </summary>
public class PriorAuthRow
{
    public string RequestId { get; set; } = string.Empty;
    public string PayerName { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string CoverageId { get; set; } = string.Empty;
    public string? MemberId { get; set; }
    public string OrganizationId { get; set; } = string.Empty;
    public string? OrganizationNpi { get; set; }
    public string? PractitionerId { get; set; }
    public string? PractitionerNpi { get; set; }
    /// <summary>routine | urgent</summary>
    public string Priority { get; set; } = "routine";
    /// <summary>approved | partially-approved | denied | pended | not-required | cancelled | contact-payer | error</summary>
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationNumber { get; set; }
    /// <summary>1 = first submission, +1 for every update / cancel.</summary>
    public int VersionNo { get; set; }
    /// <summary>JSON array of order ids, e.g. ["ord-1001"].</summary>
    public string? OrderIdsJson { get; set; }
    /// <summary>JSON object orderId -> line number, e.g. {"ord-1001":1}.</summary>
    public string? LineNumbersJson { get; set; }
    /// <summary>The PriorAuthDecision as JSON (same structure as the response of POST /api/v1/pas/requests).</summary>
    public string? DecisionJson { get; set; }
    /// <summary>Last PAS Bundle sent to the payer (FHIR JSON).</summary>
    public string? LastRequestBundle { get; set; }
    /// <summary>Last Bundle received from the payer (FHIR JSON).</summary>
    public string? LastResponseBundle { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
