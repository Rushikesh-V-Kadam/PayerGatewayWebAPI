namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: ClaimResponse (read from the payer), https://hl7.org/fhir/R4/claimresponse.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>FHIR ClaimResponse (PAS profile-claimresponse): the payer's decision.</summary>
public class ClaimResponse
{
    public string ResourceType { get; set; } = "ClaimResponse";
    // R4: ClaimResponse.id 0..1 - optional
    public string? Id { get; set; }
    // R4: ClaimResponse.created 1..1 - required
    public string? Created { get; set; }
    // R4: ClaimResponse.outcome 1..1 - required
    /// <summary>queued | complete | error | partial</summary>
    public string? Outcome { get; set; }
    // R4: ClaimResponse.preAuthRef 0..1 - optional
    /// <summary>Authorization number for the whole request.</summary>
    public string? PreAuthRef { get; set; }
    // R4: ClaimResponse.preAuthPeriod 0..1 - optional
    public Period? PreAuthPeriod { get; set; }
    // R4: ClaimResponse.item 0..* - optional
    public List<ClaimResponseItem> Item { get; set; } = new();
    // R4: ClaimResponse.error 0..* - optional
    public List<ClaimResponseError> Error { get; set; } = new();
    // R4: ClaimResponse.processNote 0..* - optional
    public List<ClaimResponseProcessNote> ProcessNote { get; set; } = new();
}
