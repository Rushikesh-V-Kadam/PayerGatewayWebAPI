using System.Text.Json.Nodes;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Bundle, https://hl7.org/fhir/R4/bundle.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>
/// FHIR Bundle. Entries hold the resource as raw JSON so any resource type can travel through the gateway
/// unchanged (read a typed copy with FhirJson.Read when a field is needed).
/// </summary>
public class Bundle
{
    public string ResourceType { get; set; } = "Bundle";
    // R4: Bundle.id 0..1 - optional
    public string? Id { get; set; }
    // R4: Bundle.meta 0..1 - optional
    public Meta? Meta { get; set; }
    // R4: Bundle.identifier 0..1 - optional
    public Identifier? Identifier { get; set; }
    // R4: Bundle.type 1..1 - required
    /// <summary>collection | searchset | history ...</summary>
    public string? Type { get; set; }
    // R4: Bundle.timestamp 0..1 - optional
    public string? Timestamp { get; set; }
    // R4: Bundle.total 0..1 - optional
    public int? Total { get; set; }
    // R4: Bundle.entry 0..* - optional
    public List<BundleEntry> Entry { get; set; } = new();

    /// <summary>All entry resources of one type, as raw JSON.</summary>
    public List<JsonObject> ResourcesOfType(string resourceType)
    {
        List<JsonObject> result = new List<JsonObject>();
        foreach (BundleEntry entry in Entry)
        {
            if (entry.Resource == null)
            {
                continue;
            }
            JsonValue? type = entry.Resource["resourceType"] as JsonValue;
            string? typeText;
            if (type != null && type.TryGetValue<string>(out typeText) && typeText == resourceType)
            {
                result.Add(entry.Resource);
            }
        }
        return result;
    }
}
