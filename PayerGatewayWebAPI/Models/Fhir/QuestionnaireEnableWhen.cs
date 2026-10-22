using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Questionnaire.item.enableWhen (each entry), https://hl7.org/fhir/R4/questionnaire.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>"Only ask this question when ..." rule of a Questionnaire item.</summary>
public class QuestionnaireEnableWhen
{
    // R4: Questionnaire.item.enableWhen.question 1..1 - required
    /// <summary>linkId of the question the rule looks at.</summary>
    public string Question { get; set; } = string.Empty;
    // R4: Questionnaire.item.enableWhen.operator 1..1 - required
    /// <summary>exists | = | != | &gt; | &lt; | &gt;= | &lt;=</summary>
    public string Operator { get; set; } = "=";
    // R4: Questionnaire.item.enableWhen.answer[x] 1..1 (only one answer* is used) - required
    public bool? AnswerBoolean { get; set; }
    // R4: Questionnaire.item.enableWhen.answer[x] 1..1 (only one answer* is used) - required
    public decimal? AnswerDecimal { get; set; }
    // R4: Questionnaire.item.enableWhen.answer[x] 1..1 (only one answer* is used) - required
    public int? AnswerInteger { get; set; }
    // R4: Questionnaire.item.enableWhen.answer[x] 1..1 (only one answer* is used) - required
    public string? AnswerDate { get; set; }
    // R4: Questionnaire.item.enableWhen.answer[x] 1..1 (only one answer* is used) - required
    public string? AnswerDateTime { get; set; }
    // R4: Questionnaire.item.enableWhen.answer[x] 1..1 (only one answer* is used) - required
    public string? AnswerString { get; set; }
    // R4: Questionnaire.item.enableWhen.answer[x] 1..1 (only one answer* is used) - required
    public Coding? AnswerCoding { get; set; }
    // R4: Questionnaire.item.enableWhen.answer[x] 1..1 (only one answer* is used) - required
    public Quantity? AnswerQuantity { get; set; }

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }
}
