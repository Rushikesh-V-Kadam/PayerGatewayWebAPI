using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Coverage.class (each entry), https://hl7.org/fhir/R4/coverage.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>Plan / group of a Coverage.</summary>
public class CoverageClass
{
    // R4: Coverage.class.type 1..1 - required
    public CodeableConcept? Type { get; set; }
    // R4: Coverage.class.value 1..1 - required
    public string Value { get; set; } = string.Empty;
    // R4: Coverage.class.name 0..1 - optional
    public string? Name { get; set; }

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }
}
