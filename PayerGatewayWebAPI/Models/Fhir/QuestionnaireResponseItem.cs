namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: QuestionnaireResponse.item (each answer group), https://hl7.org/fhir/R4/questionnaireresponse.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>One question (or group) of a QuestionnaireResponse, in the same tree shape as the Questionnaire.</summary>
public class QuestionnaireResponseItem
{
    // R4: QuestionnaireResponse.item.linkId 1..1 - required
    public string LinkId { get; set; } = string.Empty;
    // R4: QuestionnaireResponse.item.text 0..1 - optional
    public string? Text { get; set; }
    // R4: QuestionnaireResponse.item.answer 0..* - optional
    public List<QuestionnaireResponseAnswer> Answer { get; set; } = new();
    // R4: QuestionnaireResponse.item.item 0..* - optional
    /// <summary>Child items (for groups).</summary>
    public List<QuestionnaireResponseItem> Item { get; set; } = new();
}
