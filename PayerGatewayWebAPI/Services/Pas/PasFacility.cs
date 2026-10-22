namespace PayerGatewayWebAPI.Services.Pas;

/// <summary>
/// The facility of a PAS request, read from orderLocationCode (Location/{orderLocationCode}):
/// the facility Organization (Claim.provider) and the facility's CMS place-of-service code (Claim.item.locationCodeableConcept).
/// </summary>
public class PasFacility
{
    /// <summary>Id of the facility Organization (Location.managingOrganization), e.g. facility-loc-main.</summary>
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>CMS place-of-service code of the facility (Location.type), e.g. 65. Null when the location has none.</summary>
    public string? PlaceOfServiceCode { get; set; }
}
