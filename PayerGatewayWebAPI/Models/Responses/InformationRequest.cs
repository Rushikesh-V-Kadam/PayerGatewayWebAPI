namespace PayerGatewayWebAPI.Models.Responses;

/// <summary>One payer request for more information (a CDex / PAS Task).</summary>
public class InformationRequest
{
    /// <summary>Payer tracking id. Send it back with the attachments.</summary>
    /// <example>TRK-88421</example>
    public string TrackingId { get; set; } = string.Empty;

    /// <example>task-501</example>
    public string? TaskId { get; set; }

    /// <example>2026-10-05</example>
    public string? DueDate { get; set; }

    /// <summary>requested | in-progress | completed | cancelled ...</summary>
    /// <example>requested</example>
    public string Status { get; set; } = "requested";

    /// <summary>Document types needed (LOINC).</summary>
    public List<DocumentNeeded> DocumentsNeeded { get; set; } = new();

    /// <summary>Payer questionnaires to complete: fill them in with the payer's SMART DTR app, then send the saved form (GET /dtr/saved-forms) here.</summary>
    /// <example>[]</example>
    public List<string> QuestionnairesNeeded { get; set; } = new();
}
