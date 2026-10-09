namespace PayerGatewayWebAPI.Models.Storage;

/// <summary>
/// A DTR form exactly as it is sent to / returned by the EHR data API (dtr-forms) = one row of table PA_DTR_FORM.
/// Lists are sent as JSON text (the "...Json" properties) so the EHR can store them unchanged in CLOB columns.
/// </summary>
public class DtrFormRow
{
    public string FormId { get; set; } = string.Empty;
    public string PayerName { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string? CoverageId { get; set; }
    /// <summary>JSON array of order ids, e.g. ["ord-1001"].</summary>
    public string? OrderIdsJson { get; set; }
    public string QuestionnaireUrl { get; set; } = string.Empty;
    /// <summary>The payer Questionnaire (FHIR JSON).</summary>
    public string QuestionnaireJson { get; set; } = string.Empty;
    /// <summary>new | in-progress | completed</summary>
    public string Status { get; set; } = "new";
    public string? QuestionnaireResponseId { get; set; }
    /// <summary>JSON array of the latest plain answers, e.g. [{"linkId":"egfr","values":["12"]}].</summary>
    public string? AnswersJson { get; set; }
    public bool Adaptive { get; set; }
    public bool PayerFinished { get; set; }
    public string? AdaptiveResponseJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
