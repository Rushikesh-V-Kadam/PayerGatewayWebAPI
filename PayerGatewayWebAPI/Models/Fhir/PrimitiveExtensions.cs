namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Extensions on a primitive value (JSON "_name"), https://hl7.org/fhir/R4/json.html#primitive
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>
/// Extensions on a primitive value, written as "_name" in FHIR JSON
/// (used for Subscription._criteria and Subscription.channel._payload).
/// </summary>
public class PrimitiveExtensions
{
    // R4: Element.extension 0..* - optional
    public List<Extension> Extension { get; set; } = new();
}
