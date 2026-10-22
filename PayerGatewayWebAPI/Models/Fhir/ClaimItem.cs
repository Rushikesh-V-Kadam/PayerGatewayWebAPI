namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Claim.item (each line), https://hl7.org/fhir/R4/claim.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>Claim.item: one requested service (one order). Sequence numbers stay the same across updates.</summary>
public class ClaimItem
{
    // R4: Claim.item.extension 0..* - optional
    public List<Extension> Extension { get; set; } = new();
    // R4: Claim.item.modifierExtension 0..* - optional
    public List<Extension> ModifierExtension { get; set; } = new();
    // R4: Claim.item.sequence 1..1 - required
    public int Sequence { get; set; }
    // R4: Claim.item.careTeamSequence 0..* - optional
    public List<int> CareTeamSequence { get; set; } = new();
    // R4: Claim.item.diagnosisSequence 0..* - optional
    public List<int> DiagnosisSequence { get; set; } = new();
    // R4: Claim.item.informationSequence 0..* - optional
    public List<int> InformationSequence { get; set; } = new();
    // R4: Claim.item.category 0..1 - optional
    /// <summary>X12 1365 service type.</summary>
    public CodeableConcept? Category { get; set; }
    // R4: Claim.item.productOrService 1..1 - required
    public CodeableConcept? ProductOrService { get; set; }
    // R4: Claim.item.serviced[x] 0..1 (date or period, not both) - optional
    public string? ServicedDate { get; set; }
    // R4: Claim.item.serviced[x] 0..1 (date or period, not both) - optional
    public Period? ServicedPeriod { get; set; }
    // R4: Claim.item.location[x] 0..1 - optional
    public CodeableConcept? LocationCodeableConcept { get; set; }
    // R4: Claim.item.quantity 0..1 - optional
    public Quantity? Quantity { get; set; }
}
