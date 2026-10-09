using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

/// <summary>FHIR Coding: one code from one code system (e.g. LOINC 18748-4).</summary>
public class Coding
{
    public string? System { get; set; }
    public string? Code { get; set; }
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
