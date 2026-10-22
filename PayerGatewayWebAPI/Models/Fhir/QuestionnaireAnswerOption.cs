using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Questionnaire.item.answerOption (each entry), https://hl7.org/fhir/R4/questionnaire.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>One allowed answer of a choice question.</summary>
public class QuestionnaireAnswerOption
{
    // R4: Questionnaire.item.answerOption.value[x] 1..1 (only one value* is used) - required
    public Coding? ValueCoding { get; set; }
    // R4: Questionnaire.item.answerOption.value[x] 1..1 (only one value* is used) - required
    public string? ValueString { get; set; }
    // R4: Questionnaire.item.answerOption.value[x] 1..1 (only one value* is used) - required
    public int? ValueInteger { get; set; }
    // R4: Questionnaire.item.answerOption.value[x] 1..1 (only one value* is used) - required
    public string? ValueDate { get; set; }
    // R4: Questionnaire.item.answerOption.initialSelected 0..1 - optional
    public bool? InitialSelected { get; set; }

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }
}
