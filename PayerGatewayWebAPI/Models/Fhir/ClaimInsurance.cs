namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Claim.insurance (each entry), https://hl7.org/fhir/R4/claim.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>Claim.insurance: the Coverage the request is for.</summary>
public class ClaimInsurance
{
    // R4: Claim.insurance.sequence 1..1 - required
    public int Sequence { get; set; } = 1;
    // R4: Claim.insurance.focal 1..1 - required
    public bool Focal { get; set; } = true;
    // R4: Claim.insurance.coverage 1..1 - required
    public ResourceReference? Coverage { get; set; }
}
