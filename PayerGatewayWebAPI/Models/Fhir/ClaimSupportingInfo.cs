namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Claim.supportingInfo (each entry), https://hl7.org/fhir/R4/claim.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>Claim.supportingInfo: DTR forms, documents or a free-text note sent with the request.</summary>
public class ClaimSupportingInfo
{
    // R4: Claim.supportingInfo.sequence 1..1 - required
    public int Sequence { get; set; }
    // R4: Claim.supportingInfo.category 1..1 - required
    public CodeableConcept? Category { get; set; }
    // R4: Claim.supportingInfo.value[x] 0..1 (string or reference, not both) - optional
    public string? ValueString { get; set; }
    // R4: Claim.supportingInfo.value[x] 0..1 (string or reference, not both) - optional
    public ResourceReference? ValueReference { get; set; }
}
