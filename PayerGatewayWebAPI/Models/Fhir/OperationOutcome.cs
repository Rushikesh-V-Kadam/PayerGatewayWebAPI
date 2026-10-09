namespace PayerGatewayWebAPI.Models.Fhir;

/// <summary>FHIR OperationOutcome: errors / warnings returned by a payer.</summary>
public class OperationOutcome
{
    public string ResourceType { get; set; } = "OperationOutcome";
    public List<OperationOutcomeIssue> Issue { get; set; } = new();

    /// <summary>Human-readable messages of all issues.</summary>
    public List<string> Messages()
    {
        List<string> messages = new List<string>();
        foreach (OperationOutcomeIssue issue in Issue)
        {
            // the message: diagnostics, else details (text / display / code), else the issue code
            string text = issue.Code;
            if (!string.IsNullOrWhiteSpace(issue.Diagnostics))
            {
                text = issue.Diagnostics;
            }
            else if (issue.Details != null && !string.IsNullOrWhiteSpace(issue.Details.BestText()))
            {
                text = issue.Details.BestText()!;
            }
            if (issue.Expression.Count > 0)
            {
                text = text + " (" + string.Join(", ", issue.Expression) + ")";
            }
            messages.Add(text);
        }
        return messages;
    }
}
