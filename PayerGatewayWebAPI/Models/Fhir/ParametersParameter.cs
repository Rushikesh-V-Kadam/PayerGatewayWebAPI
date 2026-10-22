using System.Text.Json.Nodes;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Parameters.parameter (each entry), https://hl7.org/fhir/R4/parameters.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>One parameter of a Parameters resource: a value, a resource, or child parts.</summary>
public class ParametersParameter
{
    // R4: Parameters.parameter.name 1..1 - required
    public string Name { get; set; } = string.Empty;
    // R4: Parameters.parameter.value[x] 0..1 (only one value* is used) - optional
    public string? ValueString { get; set; }
    // R4: Parameters.parameter.value[x] 0..1 (only one value* is used) - optional
    public string? ValueCode { get; set; }
    // R4: Parameters.parameter.value[x] 0..1 (only one value* is used) - optional
    public bool? ValueBoolean { get; set; }
    // R4: Parameters.parameter.value[x] 0..1 (only one value* is used) - optional
    public string? ValueCanonical { get; set; }
    // R4: Parameters.parameter.value[x] 0..1 (only one value* is used) - optional
    public Identifier? ValueIdentifier { get; set; }
    // R4: Parameters.parameter.value[x] 0..1 (only one value* is used) - optional
    public CodeableConcept? ValueCodeableConcept { get; set; }

    // R4: Parameters.parameter.resource 0..1 - optional
    /// <summary>A whole resource as raw JSON.</summary>
    public JsonObject? Resource { get; set; }

    // R4: Parameters.parameter.part 0..* - optional
    public List<ParametersParameter> Part { get; set; } = new();
}
