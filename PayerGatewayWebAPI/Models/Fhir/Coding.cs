using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Coding (data type), https://hl7.org/fhir/R4/datatypes.html#Coding
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>FHIR Coding: one code from one code system (e.g. LOINC 18748-4).</summary>
public class Coding
{
    // R4: Coding.system 0..1 - optional
    public string? System { get; set; }
    // R4: Coding.code 0..1 - optional
    public string? Code { get; set; }
    // R4: Coding.display 0..1 - optional
    public string? Display { get; set; }

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }

    /// <summary>Empty coding (used by the JSON serializer).</summary>
    public Coding()
    {
    }

    /// <summary>Creates a coding.</summary>
    public Coding(string? system, string? code, string? display = null)
    {
        System = system;
        Code = code;
        Display = display;
    }
}
