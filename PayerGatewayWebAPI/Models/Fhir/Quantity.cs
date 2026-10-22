using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Quantity (data type), https://hl7.org/fhir/R4/datatypes.html#Quantity
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>FHIR Quantity: an amount with an optional unit.</summary>
public class Quantity
{
    // R4: Quantity.value 0..1 - optional
    public decimal? Value { get; set; }
    // R4: Quantity.unit 0..1 - optional
    public string? Unit { get; set; }
    // R4: Quantity.system 0..1 - optional
    public string? System { get; set; }
    // R4: Quantity.code 0..1 - optional
    public string? Code { get; set; }

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }
}
