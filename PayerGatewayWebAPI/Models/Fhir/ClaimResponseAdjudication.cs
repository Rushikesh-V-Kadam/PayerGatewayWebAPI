namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: ClaimResponse.item.adjudication (each entry), https://hl7.org/fhir/R4/claimresponse.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>
/// ClaimResponse.item.adjudication. PAS 2.0.1 puts the decision of a line here, in the reviewAction extension
/// (X12 action code, authorization number, reason).
/// </summary>
public class ClaimResponseAdjudication
{
    // R4: ClaimResponse.item.adjudication.extension 0..* - optional
    /// <summary>Holds the PAS reviewAction extension.</summary>
    public List<Extension> Extension { get; set; } = new();

    // R4: ClaimResponse.item.adjudication.category 1..1 - required
    /// <summary>Kind of adjudication; PAS uses "submitted".</summary>
    public CodeableConcept? Category { get; set; }
}
