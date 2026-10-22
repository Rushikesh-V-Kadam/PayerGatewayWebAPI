using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Meta (data type), https://hl7.org/fhir/R4/resource.html#Meta
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>FHIR Meta: the profiles a resource claims to follow.</summary>
public class Meta
{
    // R4: Meta.profile 0..* - optional
    public List<string> Profile { get; set; } = new();
    // R4: Meta.lastUpdated 0..1 - optional
    public string? LastUpdated { get; set; }

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }
}
