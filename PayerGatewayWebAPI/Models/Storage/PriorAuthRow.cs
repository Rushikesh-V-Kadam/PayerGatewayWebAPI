using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Storage;

/// <summary>
/// A prior authorization request exactly as it is sent to / returned by the EHR data API (POST prior-auths,
/// GET prior-auths/{requestId}) = one row of table PA_PRIOR_AUTH. Lists, the decision and the FHIR Bundles are sent as
/// JSON text so the EHR can store them unchanged in CLOB columns.
/// CLIENT-EHR: the JSON names (JsonPropertyName) are the client's own request body names (RequestID, OrderIDsJSON,
/// OrganizationNPI ...), not the gateway's usual camelCase. Reading accepts any letter case.
/// </summary>
public class PriorAuthRow
{
    /// <summary>Column REQUEST_ID (key).</summary>
    [JsonPropertyName("RequestID")]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Column INSURANCE_PLAN_ID: insurance plan id of the payer.</summary>
    [JsonPropertyName("InsurancePlanID")]
    public int InsurancePlanId { get; set; }
    /// <summary>Column PATIENT_ID.</summary>
    [JsonPropertyName("PatientID")]
    public string PatientId { get; set; } = string.Empty;
    /// <summary>Column COVERAGE_ID.</summary>
    [JsonPropertyName("CoverageID")]
    public string CoverageId { get; set; } = string.Empty;
    /// <summary>Column MEMBER_ID.</summary>
    [JsonPropertyName("MemberID")]
    public string? MemberId { get; set; }
    /// <summary>Column ORGANIZATION_ID.</summary>
    [JsonPropertyName("OrganizationID")]
    public string OrganizationId { get; set; } = string.Empty;
    /// <summary>Column ORGANIZATION_NPI.</summary>
    [JsonPropertyName("OrganizationNPI")]
    public string? OrganizationNpi { get; set; }
    /// <summary>Column PRACTITIONER_ID.</summary>
    [JsonPropertyName("PractitionerID")]
    public string? PractitionerId { get; set; }
    /// <summary>Column PRACTITIONER_NPI.</summary>
    [JsonPropertyName("PractitionerNPI")]
    public string? PractitionerNpi { get; set; }
    /// <summary>routine | urgent</summary>
    [JsonPropertyName("Priority")]
    public string Priority { get; set; } = "routine";
    /// <summary>approved | partially-approved | denied | pended | not-required | cancelled | contact-payer | error</summary>
    [JsonPropertyName("Status")]
    public string Status { get; set; } = string.Empty;
    /// <summary>Column AUTHORIZATION_NUMBER: filled when the payer approved.</summary>
    [JsonPropertyName("AuthorizationNumber")]
    public string? AuthorizationNumber { get; set; }
    /// <summary>1 = first submission, +1 for every update / cancel.</summary>
    [JsonPropertyName("VersionNo")]
    public int VersionNo { get; set; }
    /// <summary>JSON array of order ids, e.g. ["ord-1001"].</summary>
    [JsonPropertyName("OrderIDsJSON")]
    public string? OrderIdsJson { get; set; }
    /// <summary>JSON object orderId -> line number, e.g. {"ord-1001":1}.</summary>
    [JsonPropertyName("LineNumbersJSON")]
    public string? LineNumbersJson { get; set; }
    /// <summary>The PriorAuthDecision as JSON (same structure as the response of POST /pas/requests).</summary>
    [JsonPropertyName("DecisionJSON")]
    public string? DecisionJson { get; set; }
    /// <summary>Last PAS Bundle sent to the payer (FHIR JSON).</summary>
    [JsonPropertyName("LastRequestBundle")]
    public string? LastRequestBundle { get; set; }
    /// <summary>Last Bundle received from the payer (FHIR JSON).</summary>
    [JsonPropertyName("LastResponseBundle")]
    public string? LastResponseBundle { get; set; }
    /// <summary>Column SUBMITTED_AT (UTC).</summary>
    [JsonPropertyName("SubmittedAt")]
    public DateTime SubmittedAt { get; set; }
    /// <summary>Column UPDATED_AT (UTC).</summary>
    [JsonPropertyName("UpdatedAt")]
    public DateTime UpdatedAt { get; set; }
}
