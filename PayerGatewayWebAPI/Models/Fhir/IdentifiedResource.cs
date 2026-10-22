namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Organization / Practitioner / Patient (only id and identifiers), https://hl7.org/fhir/R4/resourcelist.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>
/// Minimal typed view of Organization / Practitioner / Patient: only the id and identifiers
/// (the gateway needs the NPI). The full resource travels as raw JSON.
/// </summary>
public class IdentifiedResource
{
    public string? ResourceType { get; set; }
    // R4: <Resource>.id 0..1 - optional
    public string? Id { get; set; }
    // R4: Organization / Practitioner / Patient .identifier 0..* - optional
    public List<Identifier> Identifier { get; set; } = new();

    /// <summary>Value of the identifier with this system (e.g. the NPI), or null.</summary>
    public string? IdentifierValue(string system)
    {
        foreach (Identifier identifier in Identifier)
        {
            if (identifier.System == system)
            {
                return identifier.Value;
            }
        }
        return null;
    }
}
