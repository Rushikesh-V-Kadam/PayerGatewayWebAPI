using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Extension, https://hl7.org/fhir/R4/extensibility.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>
/// FHIR Extension. Only one value[x] is set; a complex extension has child extensions instead.
/// Used to read CRD coverage-information, PAS review actions and CDex line numbers, and to write PAS / DTR extensions.
/// </summary>
public class Extension
{
    // R4: Extension.url 1..1 - required
    public string Url { get; set; } = string.Empty;

    // R4: Extension.extension 0..* - optional
    /// <summary>Child extensions. Named Children because a member cannot share the class name; written as "extension".</summary>
    [JsonPropertyName("extension")]
    public List<Extension> Children { get; set; } = new();

    // R4: Extension.value[x] 0..1 (only one value* is used) - optional
    public string? ValueString { get; set; }
    // R4: Extension.value[x] 0..1 (only one value* is used) - optional
    public string? ValueCode { get; set; }
    // R4: Extension.value[x] 0..1 (only one value* is used) - optional
    public bool? ValueBoolean { get; set; }
    // R4: Extension.value[x] 0..1 (only one value* is used) - optional
    public int? ValueInteger { get; set; }
    // R4: Extension.value[x] 0..1 (only one value* is used) - optional
    public int? ValuePositiveInt { get; set; }
    // R4: Extension.value[x] 0..1 (only one value* is used) - optional
    public string? ValueDate { get; set; }
    // R4: Extension.value[x] 0..1 (only one value* is used) - optional
    public string? ValueUrl { get; set; }
    // R4: Extension.value[x] 0..1 (only one value* is used) - optional
    public string? ValueCanonical { get; set; }
    // R4: Extension.value[x] 0..1 (only one value* is used) - optional
    public CodeableConcept? ValueCodeableConcept { get; set; }
    // R4: Extension.value[x] 0..1 (only one value* is used) - optional
    public ResourceReference? ValueReference { get; set; }
    // R4: Extension.value[x] 0..1 (only one value* is used) - optional
    public Identifier? ValueIdentifier { get; set; }
    // R4: Extension.value[x] 0..1 (only one value* is used) - optional
    public Period? ValuePeriod { get; set; }
    // R4: Extension.value[x] 0..1 (only one value* is used) - optional
    public Quantity? ValueQuantity { get; set; }

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }

    /// <summary>Empty extension (used by the JSON serializer).</summary>
    public Extension()
    {
    }

    /// <summary>Creates an extension with just its URL.</summary>
    public Extension(string url)
    {
        Url = url;
    }

    /// <summary>First child extension with this URL, or null.</summary>
    public Extension? Child(string url)
    {
        return Find(Children, url);
    }

    /// <summary>First extension with this URL in a list, or null.</summary>
    public static Extension? Find(List<Extension> extensions, string url)
    {
        foreach (Extension extension in extensions)
        {
            if (extension.Url == url)
            {
                return extension;
            }
        }
        return null;
    }
}
