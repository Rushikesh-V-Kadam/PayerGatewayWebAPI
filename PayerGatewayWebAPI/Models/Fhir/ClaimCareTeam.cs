namespace PayerGatewayWebAPI.Models.Fhir;

/// <summary>Claim.careTeam: the requesting practitioner (and the referring practitioner, when given).</summary>
public class ClaimCareTeam
{
    /// <summary>Holds the PAS careTeamClaimScope extension (required by PAS).</summary>
    public List<Extension> Extension { get; set; } = new();

    /// <summary>Number of this care team member inside the Claim (1, 2 ...).</summary>
    public int Sequence { get; set; }

    /// <summary>The practitioner.</summary>
    public ResourceReference? Provider { get; set; }

    /// <summary>primary = requesting practitioner, other = referring practitioner.</summary>
    public CodeableConcept? Role { get; set; }
}
