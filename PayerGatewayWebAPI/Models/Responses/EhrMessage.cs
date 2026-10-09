namespace PayerGatewayWebAPI.Models.Responses;

/// <summary>A message for the clinician, built from a payer card or a coverage-information reason.</summary>
public class EhrMessage
{
    /// <summary>info | warning | critical</summary>
    /// <example>info</example>
    public string Severity { get; set; } = "info";

    /// <example>Prior authorization required; clinical documentation needed</example>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Longer text of the message, when the payer gave one.</summary>
    public string? Detail { get; set; }

    /// <summary>A link that belongs to the message (coverage policy, or the DTR launch link), when there is one.</summary>
    public EhrMessageLink? Link { get; set; }
}
