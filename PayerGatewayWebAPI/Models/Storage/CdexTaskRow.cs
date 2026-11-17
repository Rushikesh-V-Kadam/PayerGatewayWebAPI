using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Storage;

/// <summary>
/// A payer information request (FHIR Task) exactly as it is sent to / returned by the client's APIs
/// (POST savecdxdtl, GET getcdxdtl under Ehr:DataPath) = one row of table PA_CDEX_TASK.
/// CLIENT-EHR: the JSON names (JsonPropertyName) are the client's own names (InsurancePlanID, TaskID, TaskJSON ...),
/// not the gateway's usual camelCase. Reading accepts any letter case.
/// </summary>
public class CdexTaskRow
{
    /// <summary>Column INSURANCE_PLAN_ID (key, part 1): insurance plan id of the payer that sent the Task.</summary>
    [JsonPropertyName("InsurancePlanID")]
    public int InsurancePlanId { get; set; }

    /// <summary>Column TASK_ID (key, part 2): the payer's Task id.</summary>
    [JsonPropertyName("TaskID")]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>Column REQUEST_ID: the prior authorization request the Task belongs to.</summary>
    [JsonPropertyName("RequestID")]
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Column TRACKING_ID: sent back to the payer with the documents.</summary>
    [JsonPropertyName("TrackingID")]
    public string TrackingId { get; set; } = string.Empty;

    /// <summary>Column TRACKING_SYSTEM: system of the tracking id, sent back unchanged.</summary>
    [JsonPropertyName("TrackingSystem")]
    public string? TrackingSystem { get; set; }

    /// <summary>Column STATUS: Task status (requested | in-progress | completed | cancelled ...).</summary>
    [JsonPropertyName("Status")]
    public string? Status { get; set; }

    /// <summary>Column DUE_DATE: date the payer wants the documents by, as the payer sent it.</summary>
    [JsonPropertyName("DueDate")]
    public string? DueDate { get; set; }

    /// <summary>Column TASK_JSON: the FHIR Task as received (JSON text, stored unchanged).</summary>
    [JsonPropertyName("TaskJSON")]
    public string TaskJson { get; set; } = string.Empty;

    /// <summary>Column RECEIVED_AT: when the gateway stored this version of the Task (UTC).</summary>
    [JsonPropertyName("ReceivedAt")]
    public DateTime ReceivedAt { get; set; }
}
