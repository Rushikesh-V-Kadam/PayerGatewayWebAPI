namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: OperationOutcome.issue (each entry), https://hl7.org/fhir/R4/operationoutcome.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>One issue of an OperationOutcome.</summary>
public class OperationOutcomeIssue
{
    // R4: OperationOutcome.issue.severity 1..1 - required
    /// <summary>fatal | error | warning | information</summary>
    public string Severity { get; set; } = "error";

    // R4: OperationOutcome.issue.code 1..1 - required
    /// <summary>Kind of issue, e.g. invalid, required, processing.</summary>
    public string Code { get; set; } = "processing";

    // R4: OperationOutcome.issue.details 0..1 - optional
    /// <summary>The message as a coded concept; many payers put the text here (details.text).</summary>
    public CodeableConcept? Details { get; set; }

    // R4: OperationOutcome.issue.diagnostics 0..1 - optional
    /// <summary>The message as plain text.</summary>
    public string? Diagnostics { get; set; }

    // R4: OperationOutcome.issue.expression 0..* - optional
    /// <summary>Where in the request the problem is (FHIRPath).</summary>
    public List<string> Expression { get; set; } = new();
}
