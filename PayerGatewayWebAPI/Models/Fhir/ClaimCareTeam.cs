namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Claim.careTeam (each entry), https://hl7.org/fhir/R4/claim.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>Claim.careTeam: the requesting practitioner (and the referring practitioner, when given).</summary>
public class ClaimCareTeam
{
    // R4: Claim.careTeam.extension 0..* - optional
    /// <summary>Holds the PAS careTeamClaimScope extension (required by PAS).</summary>
    public List<Extension> Extension { get; set; } = new();

    // R4: Claim.careTeam.sequence 1..1 - required
    /// <summary>Number of this care team member inside the Claim (1, 2 ...).</summary>
    public int Sequence { get; set; }

    // R4: Claim.careTeam.provider 1..1 - required
    /// <summary>The practitioner.</summary>
    public ResourceReference? Provider { get; set; }

    // R4: Claim.careTeam.role 0..1 - optional
    /// <summary>primary = requesting practitioner, other = referring practitioner.</summary>
    public CodeableConcept? Role { get; set; }
}
