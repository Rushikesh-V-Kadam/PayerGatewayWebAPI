namespace PayerGatewayWebAPI.Models.Entities;

/// <summary>One order-sign event and the answer returned for it (table PA_CRD_EVENT). A repeated eventId gets the same answer.</summary>
public class CrdEventRecord
{
    /// <summary>The EHR's id of the signing event (key).</summary>
    public string EventId { get; set; } = string.Empty;
    /// <summary>EHR patient id.</summary>
    public string PatientId { get; set; } = string.Empty;
    /// <summary>Payer that was asked.</summary>
    public string PayerName { get; set; } = string.Empty;
    /// <summary>The OrderSignResponse as JSON.</summary>
    public string ResponseJson { get; set; } = string.Empty;
    /// <summary>When the event was answered (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
