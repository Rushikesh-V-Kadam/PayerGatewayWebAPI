using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Questionnaire.item (each question), https://hl7.org/fhir/R4/questionnaire.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>One question or group of a Questionnaire.</summary>
public class QuestionnaireItem
{
    // R4: Questionnaire.item.linkId 1..1 - required
    public string LinkId { get; set; } = string.Empty;
    // R4: Questionnaire.item.prefix 0..1 - optional
    public string? Prefix { get; set; }
    // R4: Questionnaire.item.text 0..1 - optional
    public string? Text { get; set; }
    // R4: Questionnaire.item.type 1..1 - required
    /// <summary>group | display | boolean | decimal | integer | date | dateTime | time | string | text | url | choice | open-choice | attachment | reference | quantity</summary>
    public string Type { get; set; } = "string";
    // R4: Questionnaire.item.required 0..1 - optional
    public bool? Required { get; set; }
    // R4: Questionnaire.item.repeats 0..1 - optional
    public bool? Repeats { get; set; }
    // R4: Questionnaire.item.readOnly 0..1 - optional
    public bool? ReadOnly { get; set; }
    // R4: Questionnaire.item.maxLength 0..1 - optional
    public int? MaxLength { get; set; }
    // R4: Questionnaire.item.enableBehavior 0..1 - optional
    /// <summary>all | any: how several enableWhen conditions combine.</summary>
    public string? EnableBehavior { get; set; }
    // R4: Questionnaire.item.enableWhen 0..* - optional
    public List<QuestionnaireEnableWhen> EnableWhen { get; set; } = new();
    // R4: Questionnaire.item.answerOption 0..* - optional
    public List<QuestionnaireAnswerOption> AnswerOption { get; set; } = new();
    // R4: Questionnaire.item.answerValueSet 0..1 - optional
    public string? AnswerValueSet { get; set; }
    // R4: Questionnaire.item.initial 0..* - optional
    public List<QuestionnaireInitial> Initial { get; set; } = new();
    // R4: Questionnaire.item.item 0..* - optional
    public List<QuestionnaireItem> Item { get; set; } = new();

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }
}
