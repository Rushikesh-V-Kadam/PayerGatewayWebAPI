using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Reference (data type), https://hl7.org/fhir/R4/references.html#Reference
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>FHIR Reference, e.g. { "reference": "Patient/pat-1" } or a reference by identifier.</summary>
public class ResourceReference
{
    // R4: Reference.reference 0..1 - optional
    public string? Reference { get; set; }
    // R4: Reference.type 0..1 - optional
    public string? Type { get; set; }
    // R4: Reference.identifier 0..1 - optional
    public Identifier? Identifier { get; set; }
    // R4: Reference.display 0..1 - optional
    public string? Display { get; set; }

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }

    /// <summary>Empty reference (used by the JSON serializer).</summary>
    public ResourceReference()
    {
    }

    /// <summary>Creates a relative reference such as "Patient/pat-1".</summary>
    public ResourceReference(string reference)
    {
        Reference = reference;
    }

    /// <summary>The id part of "Type/id", or null.</summary>
    public string? GetId()
    {
        if (string.IsNullOrEmpty(Reference))
        {
            return null;
        }
        string[] parts = Reference.Split('/');
        return parts[parts.Length - 1];
    }

    /// <summary>The type part of "Type/id" (also for absolute URLs), or null.</summary>
    public string? GetResourceType()
    {
        if (string.IsNullOrEmpty(Reference))
        {
            return null;
        }
        string[] parts = Reference.Split('/');
        if (parts.Length < 2)
        {
            return null;
        }
        return parts[parts.Length - 2];
    }
}
