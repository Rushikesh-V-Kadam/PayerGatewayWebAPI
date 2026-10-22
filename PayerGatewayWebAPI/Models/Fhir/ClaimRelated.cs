namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Claim.related (each entry), https://hl7.org/fhir/R4/claim.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>Claim.related: an earlier authorization this request renews (relationship "prior", reference = its number).</summary>
public class ClaimRelated
{
    // R4: Claim.related.relationship 0..1 - optional
    public CodeableConcept? Relationship { get; set; }
    // R4: Claim.related.reference 0..1 - optional
    public Identifier? Reference { get; set; }
}
