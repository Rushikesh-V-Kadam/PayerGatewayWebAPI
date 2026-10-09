using PayerGatewayWebAPI.Models.Requests;

namespace PayerGatewayWebAPI.Models.Entities;

/// <summary>
/// One payer questionnaire fetched for an order (table PA_DTR_FORM). Keeps the Questionnaire so the gateway can
/// show it as simple questions and build a correct QuestionnaireResponse from plain answers.
/// </summary>
public class DtrFormRecord
{
    /// <summary>Form id created by the gateway (key), e.g. "frm-235b7ad69e85".</summary>
    public string FormId { get; set; } = string.Empty;
    /// <summary>Payer the questionnaire came from.</summary>
    public string PayerName { get; set; } = string.Empty;
    /// <summary>EHR patient id.</summary>
    public string PatientId { get; set; } = string.Empty;
    /// <summary>Insurance the form is for.</summary>
    public string? CoverageId { get; set; }
    /// <summary>Orders the form is for.</summary>
    public List<string> OrderIds { get; set; } = new();
    /// <summary>Canonical URL of the payer Questionnaire.</summary>
    public string QuestionnaireUrl { get; set; } = string.Empty;
    /// <summary>The payer Questionnaire (FHIR JSON).</summary>
    public string QuestionnaireJson { get; set; } = string.Empty;
    /// <summary>new | in-progress | completed</summary>
    public string Status { get; set; } = "new";
    /// <summary>Id of the QuestionnaireResponse saved at the FHIR API.</summary>
    public string? QuestionnaireResponseId { get; set; }
    /// <summary>The latest plain answers (to show them again).</summary>
    public List<DtrAnswerInput> Answers { get; set; } = new();
    /// <summary>true = adaptive questionnaire: the payer adds questions with $next-question.</summary>
    public bool Adaptive { get; set; }
    /// <summary>Adaptive only: true when the payer said there are no more questions (status completed).</summary>
    public bool PayerFinished { get; set; }
    /// <summary>Adaptive only: the last QuestionnaireResponse returned by the payer's $next-question.</summary>
    public string? AdaptiveResponseJson { get; set; }
    /// <summary>When the form was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>When the form was last saved (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
