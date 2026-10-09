using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

/// <summary>FHIR Identifier: a business id (NPI, member id, tracking id...).</summary>
public class Identifier
{
    public CodeableConcept? Type { get; set; }
    public string? System { get; set; }
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
