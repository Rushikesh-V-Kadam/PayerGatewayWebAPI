namespace PayerGatewayWebAPI.Models.Entities;

/// <summary>Latest CRD decision of an order (table PA_COVERAGE_DECISION).</summary>
public class CoverageDecisionRecord
{
    /// <summary>EHR order id (key: one decision per order).</summary>
    public string OrderId { get; set; } = string.Empty;
    /// <summary>The order-sign event that produced the decision.</summary>
    public string EventId { get; set; } = string.Empty;
    /// <summary>Payer that answered.</summary>
    public string? PayerName { get; set; }
    /// <summary>EHR patient id.</summary>
    public string PatientId { get; set; } = string.Empty;
    /// <summary>The payer's id for this answer (needed for DTR).</summary>
    public string? CoverageAssertionId { get; set; }
    /// <summary>covered | not-covered | conditional | unknown</summary>
    public string Covered { get; set; } = string.Empty;
    /// <summary>no-auth | auth-needed | satisfied | performpa | conditional</summary>
    public string? PriorAuthNeeded { get; set; }
    /// <summary>no-doc | clinical | admin | both | conditional</summary>
    public string? DocumentationNeeded { get; set; }
    /// <summary>The CoverageDecision as JSON.</summary>
    public string DecisionJson { get; set; } = string.Empty;
    /// <summary>When the decision was saved (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
