using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Coverage, https://hl7.org/fhir/R4/coverage.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>
/// FHIR Coverage (CRD profile) read from the FHIR API = the patient's insurance in the EHR. The gateway reads the
/// member id and the payer id from it and re-points Coverage.payor before sending it to a payer. Properties that are
/// not listed here are kept in OtherProperties, so the payer receives the whole Coverage.
/// </summary>
public class Coverage
{
    public string ResourceType { get; set; } = "Coverage";
    // R4: Coverage.id 0..1 - optional
    public string? Id { get; set; }
    // R4: Coverage.meta 0..1 - optional
    public Meta? Meta { get; set; }
    // R4: Coverage.identifier 0..* - optional
    public List<Identifier> Identifier { get; set; } = new();
    // R4: Coverage.status 1..1 - required
    public string? Status { get; set; }
    // R4: Coverage.type 0..1 - optional
    public CodeableConcept? Type { get; set; }
    // R4: Coverage.subscriberId 0..1 - optional
    public string? SubscriberId { get; set; }
    // R4: Coverage.beneficiary 1..1 - required
    public ResourceReference? Beneficiary { get; set; }
    // R4: Coverage.relationship 0..1 - optional
    public CodeableConcept? Relationship { get; set; }
    // R4: Coverage.period 0..1 - optional
    public Period? Period { get; set; }
    // R4: Coverage.payor 1..* - required
    /// <summary>The payer organization (display name and identifier).</summary>
    public List<ResourceReference> Payor { get; set; } = new();
    // R4: Coverage.class 0..* - optional
    public List<CoverageClass> Class { get; set; } = new();

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }

    /// <summary>
    /// The member id of the insurance, or null when the insurance has none:
    ///   1. subscriberId (the id on the member's card; the FHIR API fills it from the member id in the EHR)
    ///   2. else the identifier with type "MB" (member number).
    /// Other identifiers (policy number, group number ...) are never used as the member id.
    /// </summary>
    public string? MemberId()
    {
        if (!string.IsNullOrWhiteSpace(SubscriberId))
        {
            return SubscriberId;
        }
        Identifier? memberNumber = MemberNumberIdentifier();
        if (memberNumber != null)
        {
            return memberNumber.Value;
        }
        return null;
    }

    /// <summary>
    /// The identifier that carries the member id returned by MemberId(), or null when no identifier has that value.
    /// Used only to send the member id with the same system the insurance has; the value always comes from MemberId().
    /// The identifier with type "MB" is preferred when several identifiers have the value.
    /// </summary>
    public Identifier? MemberIdentifier()
    {
        string? memberId = MemberId();
        if (memberId == null)
        {
            return null;
        }
        Identifier? memberNumber = MemberNumberIdentifier();
        if (memberNumber != null && memberNumber.Value == memberId)
        {
            return memberNumber;
        }
        foreach (Identifier identifier in Identifier)
        {
            if (identifier.Value == memberId)
            {
                return identifier;
            }
        }
        return null;
    }

    /// <summary>The payer id of the insurance (identifier of the payor chosen by MainPayor), or null when it has none.</summary>
    public Identifier? PayorIdentifier()
    {
        ResourceReference? payor = MainPayor();
        if (payor == null || payor.Identifier == null || string.IsNullOrWhiteSpace(payor.Identifier.Value))
        {
            return null;
        }
        return payor.Identifier;
    }

    /// <summary>The payer's name on the insurance (display of the payor chosen by MainPayor), or null when it has none.</summary>
    public string? PayorDisplay()
    {
        ResourceReference? payor = MainPayor();
        if (payor == null || string.IsNullOrWhiteSpace(payor.Display))
        {
            return null;
        }
        return payor.Display;
    }

    /// <summary>
    /// The payor the payer id and the payer name are read from: the first payor that has an identifier, else the
    /// first payor, else null. Both values come from the same payor, so they always belong together.
    /// </summary>
    private ResourceReference? MainPayor()
    {
        foreach (ResourceReference payor in Payor)
        {
            if (payor.Identifier != null && !string.IsNullOrWhiteSpace(payor.Identifier.Value))
            {
                return payor;
            }
        }
        if (Payor.Count > 0)
        {
            return Payor[0];
        }
        return null;
    }

    /// <summary>The first identifier with type "MB" (member number) that has a value, or null.</summary>
    private Identifier? MemberNumberIdentifier()
    {
        foreach (Identifier identifier in Identifier)
        {
            if (identifier.Type != null && identifier.Type.HasCode("MB") && !string.IsNullOrWhiteSpace(identifier.Value))
            {
                return identifier;
            }
        }
        return null;
    }
}
