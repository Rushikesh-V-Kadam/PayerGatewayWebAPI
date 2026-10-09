namespace PayerGatewayWebAPI.Models.Storage;

/// <summary>
/// A DTR form exactly as it is sent to / returned by the EHR data API (dtr-forms) = one row of table PA_DTR_FORM.
/// Lists are sent as JSON text (the "...Json" properties) so the EHR can store them unchanged in CLOB columns.
/// </summary>
public class DtrFormRow
{
    /// <summary>Column FORM_ID (key).</summary>
    public string FormId { get; set; } = string.Empty;
    /// <summary>Column PAYER_NAME.</summary>
    public string PayerName { get; set; } = string.Empty;
    /// <summary>Column PATIENT_ID.</summary>
    public string PatientId { get; set; } = string.Empty;
    /// <summary>Column COVERAGE_ID.</summary>
    public string? CoverageId { get; set; }
    /// <summary>JSON array of order ids, e.g. ["ord-1001"].</summary>
    public string? OrderIdsJson { get; set; }
    /// <summary>Column QUESTIONNAIRE_URL.</summary>
    public string QuestionnaireUrl { get; set; } = string.Empty;
    /// <summary>The payer Questionnaire (FHIR JSON).</summary>
    public string QuestionnaireJson { get; set; } = string.Empty;
    /// <summary>new | in-progress | completed</summary>
    public string Status { get; set; } = "new";
    /// <summary>Column QUESTIONNAIRE_RESPONSE_ID: id of the filled-in form in table PA_QUESTIONNAIRE_RESPONSE.</summary>
    public string? QuestionnaireResponseId { get; set; }
    /// <summary>JSON array of the latest plain answers, e.g. [{"linkId":"egfr","values":["12"]}].</summary>
    public string? AnswersJson { get; set; }
    /// <summary>Column ADAPTIVE: true = the payer adds questions step by step.</summary>
    public bool Adaptive { get; set; }
    /// <summary>Column PAYER_FINISHED: adaptive only, true = the payer has no more questions.</summary>
    public bool PayerFinished { get; set; }
    /// <summary>Column ADAPTIVE_RESPONSE_JSON: adaptive only, the last QuestionnaireResponse from the payer (FHIR JSON).</summary>
    public string? AdaptiveResponseJson { get; set; }
    /// <summary>Column CREATED_AT (UTC).</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Column UPDATED_AT (UTC).</summary>
    public DateTime UpdatedAt { get; set; }
}
