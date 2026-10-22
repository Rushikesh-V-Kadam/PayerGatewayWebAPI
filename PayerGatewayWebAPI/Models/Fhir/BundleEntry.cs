using System.Text.Json.Nodes;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Bundle.entry, https://hl7.org/fhir/R4/bundle.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>One entry of a Bundle.</summary>
public class BundleEntry
{
    // R4: Bundle.entry.fullUrl 0..1 - optional
    public string? FullUrl { get; set; }

    // R4: Bundle.entry.resource 0..1 - optional
    /// <summary>The resource as raw JSON.</summary>
    public JsonObject? Resource { get; set; }
}
