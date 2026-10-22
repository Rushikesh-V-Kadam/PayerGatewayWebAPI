namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Claim, https://hl7.org/fhir/R4/claim.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>
/// FHIR Claim (PAS profile-claim / profile-claim-update / profile-claim-inquiry) built by the gateway.
/// use = "preauthorization". One item per order.
/// </summary>
public class Claim
{
    public string ResourceType { get; set; } = "Claim";
    // R4: Claim.id 0..1 - optional
    public string? Id { get; set; }
    // R4: Claim.meta 0..1 - optional
    public Meta? Meta { get; set; }
    // R4: Claim.identifier 0..* - optional
    public List<Identifier> Identifier { get; set; } = new();
    // R4: Claim.status 1..1 - required
    public string Status { get; set; } = "active";
    // R4: Claim.type 1..1 - required
    public CodeableConcept? Type { get; set; }
    // R4: Claim.use 1..1 - required
    public string Use { get; set; } = "preauthorization";
    // R4: Claim.patient 1..1 - required
    public ResourceReference? Patient { get; set; }
    // R4: Claim.created 1..1 - required
    public string? Created { get; set; }
    // R4: Claim.insurer 0..1 - optional
    public ResourceReference? Insurer { get; set; }
    // R4: Claim.provider 1..1 - required
    public ResourceReference? Provider { get; set; }
    // R4: Claim.priority 1..1 - required
    public CodeableConcept? Priority { get; set; }
    // R4: Claim.related 0..* - optional
    public List<ClaimRelated> Related { get; set; } = new();
    // R4: Claim.careTeam 0..* - optional
    public List<ClaimCareTeam> CareTeam { get; set; } = new();
    // R4: Claim.supportingInfo 0..* - optional
    public List<ClaimSupportingInfo> SupportingInfo { get; set; } = new();
    // R4: Claim.diagnosis 0..* - optional
    public List<ClaimDiagnosis> Diagnosis { get; set; } = new();
    // R4: Claim.insurance 1..* - required
    public List<ClaimInsurance> Insurance { get; set; } = new();
    // R4: Claim.item 0..* - optional
    public List<ClaimItem> Item { get; set; } = new();
}
