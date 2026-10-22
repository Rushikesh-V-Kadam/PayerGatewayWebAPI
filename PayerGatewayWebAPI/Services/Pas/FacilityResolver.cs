using System.Text.Json.Nodes;
using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Constants;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Models.Fhir;
using PayerGatewayWebAPI.Models.Requests;

namespace PayerGatewayWebAPI.Services.Pas;

// CLIENT-EHR: this whole class exists because of the client's EHR. Its organization has a tax id but no NPI; inside it
// are many facilities (locations), each with its own NPI, name and address. Payers identify the requester by NPI, so
// every request is sent as coming from the facility.
/// <summary>
/// Finds the facility of a PAS request. The client has many facilities, each with its own NPI; the parent organization
/// has only a tax id. The EHR keeps each facility as a location; the FHIR API serves it as Location/{orderLocationCode}
/// and, because FHIR needs the facility as an Organization (PAS Claim.provider), as the Location's managingOrganization
/// (facility NPI, the parent's tax id, partOf = the parent).
///
/// Everything comes only from orderLocationCode of the PAS request (Location/{orderLocationCode}):
///   - the facility Organization (Claim.provider): Location.managingOrganization
///   - the place of service (Claim.item.locationCodeableConcept): the CMS place-of-service code in Location.type
/// </summary>
// CLIENT-EHR: the client's orders carry no facility, so the orders' location is not read or compared.
public class FacilityResolver
{
    private readonly FhirApiClient _fhir;

    /// <summary>Created by dependency injection.</summary>
    public FacilityResolver(FhirApiClient fhir)
    {
        _fhir = fhir;
    }

    /// <summary>
    /// The facility of a PAS request: the facility Organization id (Claim.provider, 1..1) and the place-of-service code of
    /// orderLocationCode, which is required. 400 when it is missing, 404 when the location is not found, 422 when it has
    /// no managingOrganization. A location without a place-of-service code is not an error (Claim.item.location[x] is 0..1).
    /// </summary>
    public async Task<PasFacility> ResolveAsync(PriorAuthRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OrderLocationCode))
        {
            throw new ApiException(400, "orderLocationCode is required.");
        }
        return await FacilityOfCodeAsync(request.OrderLocationCode.Trim(), ct);
    }

    /// <summary>The facility of a facility code: Location/{orderLocationCode} -> managingOrganization and place of service.</summary>
    private async Task<PasFacility> FacilityOfCodeAsync(string orderLocationCode, CancellationToken ct)
    {
        JsonObject? location = await _fhir.ReadAsync("Location", orderLocationCode, ct);
        if (location == null)
        {
            throw new ApiException(404, "Facility " + orderLocationCode + " was not found at the FHIR API (Location/" + orderLocationCode + ").");
        }
        string? facilityId = ManagingOrganizationId(location);
        if (facilityId == null)
        {
            throw new ApiException(422, "Facility " + orderLocationCode + " (Location/" + orderLocationCode + ") has no managingOrganization at the FHIR API.");
        }

        PasFacility facility = new PasFacility();
        facility.OrganizationId = facilityId;
        facility.PlaceOfServiceCode = PlaceOfServiceCode(location);
        return facility;
    }

    /// <summary>The CMS place-of-service code in Location.type, or null when the location has none.</summary>
    private static string? PlaceOfServiceCode(JsonObject location)
    {
        JsonArray? types = location["type"] as JsonArray;
        if (types == null)
        {
            return null;
        }
        foreach (JsonNode? node in types)
        {
            CodeableConcept? type = FhirJson.Read<CodeableConcept>(node as JsonObject);
            if (type == null)
            {
                continue;
            }
            Coding? placeOfService = type.CodingWithSystem(CodeSystems.PlaceOfService);
            if (placeOfService != null && !string.IsNullOrWhiteSpace(placeOfService.Code))
            {
                return placeOfService.Code;
            }
        }
        return null;
    }

    /// <summary>Id of Location.managingOrganization when it is an Organization, else null.</summary>
    private static string? ManagingOrganizationId(JsonObject location)
    {
        ResourceReference? managing = FhirJson.Read<ResourceReference>(location["managingOrganization"] as JsonObject);
        if (managing == null || managing.GetResourceType() != "Organization")
        {
            return null;
        }
        return managing.GetId();
    }
}
