namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Parameters, https://hl7.org/fhir/R4/parameters.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>FHIR Parameters: input / output of FHIR operations ($questionnaire-package, $submit-attachment).</summary>
public class Parameters
{
    public string ResourceType { get; set; } = "Parameters";
    // R4: Parameters.meta 0..1 - optional
    public Meta? Meta { get; set; }
    // R4: Parameters.parameter 0..* - optional
    public List<ParametersParameter> Parameter { get; set; } = new();
}
