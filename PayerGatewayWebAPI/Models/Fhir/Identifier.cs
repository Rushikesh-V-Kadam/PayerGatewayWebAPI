using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Identifier (data type), https://hl7.org/fhir/R4/datatypes.html#Identifier
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>FHIR Identifier: a business id (NPI, member id, tracking id...).</summary>
public class Identifier
{
    // R4: Identifier.type 0..1 - optional
    public CodeableConcept? Type { get; set; }
    // R4: Identifier.system 0..1 - optional
    public string? System { get; set; }
    // R4: Identifier.value 0..1 - optional
    public string? Value { get; set; }

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }

    /// <summary>Empty identifier (used by the JSON serializer).</summary>
    public Identifier()
    {
    }

    /// <summary>Creates an identifier.</summary>
    public Identifier(string? system, string? value)
    {
        System = system;
        Value = value;
    }
}
