using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Questionnaire.item.initial (each entry), https://hl7.org/fhir/R4/questionnaire.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>A default answer the payer put on a question.</summary>
public class QuestionnaireInitial
{
    // R4: Questionnaire.item.initial.value[x] 1..1 (only one value* is used) - required
    public bool? ValueBoolean { get; set; }
    // R4: Questionnaire.item.initial.value[x] 1..1 (only one value* is used) - required
    public decimal? ValueDecimal { get; set; }
    // R4: Questionnaire.item.initial.value[x] 1..1 (only one value* is used) - required
    public int? ValueInteger { get; set; }
    // R4: Questionnaire.item.initial.value[x] 1..1 (only one value* is used) - required
    public string? ValueDate { get; set; }
    // R4: Questionnaire.item.initial.value[x] 1..1 (only one value* is used) - required
    public string? ValueDateTime { get; set; }
    // R4: Questionnaire.item.initial.value[x] 1..1 (only one value* is used) - required
    public string? ValueString { get; set; }
    // R4: Questionnaire.item.initial.value[x] 1..1 (only one value* is used) - required
    public Coding? ValueCoding { get; set; }
    // R4: Questionnaire.item.initial.value[x] 1..1 (only one value* is used) - required
    public Quantity? ValueQuantity { get; set; }

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }
}
