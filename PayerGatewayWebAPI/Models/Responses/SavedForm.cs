namespace PayerGatewayWebAPI.Models.Responses;

/// <summary>
/// A DTR form (QuestionnaireResponse) saved at the FHIR API - by the gateway forms (our questionnaire app) or by a
/// payer's SMART DTR app. Returned by GET /api/v1/dtr/saved-forms.
/// </summary>
public class SavedForm
{
    /// <summary>Use in PAS / CDex requests (questionnaireResponseIds).</summary>
    /// <example>qr-8c1d2e</example>
    public string QuestionnaireResponseId { get; set; } = string.Empty;

    /// <summary>Canonical URL of the payer's questionnaire.</summary>
    /// <example>http://example.org/Questionnaire/home-dialysis</example>
    public string? Questionnaire { get; set; }

    /// <summary>in-progress | completed | amended | stopped | entered-in-error. Only completed / amended go to PAS automatically.</summary>
    /// <example>completed</example>
    public string Status { get; set; } = string.Empty;

    /// <summary>When the form was last saved.</summary>
    /// <example>2026-10-08T10:15:00Z</example>
    public string? Authored { get; set; }

    /// <summary>The order the form belongs to (qr-context), when the form names one.</summary>
    /// <example>ord-1-hd</example>
    public string? OrderId { get; set; }
}
