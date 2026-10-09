namespace PayerGatewayWebAPI.Models.Entities;

/// <summary>A payer information request (CDex / PAS Task) received for one of our requests (table PA_CDEX_TASK).</summary>
public class CdexTaskRecord
{
    /// <summary>Payer that sent the Task (key, part 1).</summary>
    public string PayerName { get; set; } = string.Empty;
    /// <summary>The payer's Task id (key, part 2).</summary>
    public string TaskId { get; set; } = string.Empty;
    /// <summary>The prior authorization request the Task belongs to.</summary>
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Tracking id of the information request; sent back to the payer with the documents.</summary>
    public string TrackingId { get; set; } = string.Empty;
    /// <summary>System of the tracking id; sent back unchanged in $submit-attachment.</summary>
    public string? TrackingSystem { get; set; }
    /// <summary>Task status as the payer sent it: requested | in-progress | completed | cancelled ...</summary>
    public string? Status { get; set; }
    /// <summary>Date the payer wants the documents by, as the payer sent it.</summary>
    public string? DueDate { get; set; }
    /// <summary>The Task as received (FHIR JSON).</summary>
    public string TaskJson { get; set; } = string.Empty;
    /// <summary>When the gateway stored this version of the Task (UTC).</summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
