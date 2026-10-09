namespace PayerGatewayWebAPI.Models.Fhir;

/// <summary>One issue of an OperationOutcome.</summary>
public class OperationOutcomeIssue
{
    /// <summary>fatal | error | warning | information</summary>
    public string Severity { get; set; } = "error";

    /// <summary>Kind of issue, e.g. invalid, required, processing.</summary>
    public string Code { get; set; } = "processing";

    /// <summary>The message as a coded concept; many payers put the text here (details.text).</summary>
    public CodeableConcept? Details { get; set; }

    /// <summary>The message as plain text.</summary>
    public string? Diagnostics { get; set; }

    /// <summary>Where in the request the problem is (FHIRPath).</summary>
    public List<string> Expression { get; set; } = new();
}
