namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: QuestionnaireResponse.item.answer (each entry), https://hl7.org/fhir/R4/questionnaireresponse.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>One answer value. Only one value[x] is set, chosen by the question type.</summary>
public class QuestionnaireResponseAnswer
{
    // R4: QuestionnaireResponse.item.answer.extension 0..* - optional
    /// <summary>information-origin extension (auto / override / manual).</summary>
    public List<Extension> Extension { get; set; } = new();
    // R4: QuestionnaireResponse.item.answer.value[x] 0..1 (only one value* is used) - optional
    public bool? ValueBoolean { get; set; }
    // R4: QuestionnaireResponse.item.answer.value[x] 0..1 (only one value* is used) - optional
    public decimal? ValueDecimal { get; set; }
    // R4: QuestionnaireResponse.item.answer.value[x] 0..1 (only one value* is used) - optional
    public int? ValueInteger { get; set; }
    // R4: QuestionnaireResponse.item.answer.value[x] 0..1 (only one value* is used) - optional
    public string? ValueDate { get; set; }
    // R4: QuestionnaireResponse.item.answer.value[x] 0..1 (only one value* is used) - optional
    public string? ValueDateTime { get; set; }
    // R4: QuestionnaireResponse.item.answer.value[x] 0..1 (only one value* is used) - optional
    public string? ValueTime { get; set; }
    // R4: QuestionnaireResponse.item.answer.value[x] 0..1 (only one value* is used) - optional
    public string? ValueString { get; set; }
    // R4: QuestionnaireResponse.item.answer.value[x] 0..1 (only one value* is used) - optional
    public string? ValueUri { get; set; }
    // R4: QuestionnaireResponse.item.answer.value[x] 0..1 (only one value* is used) - optional
    public Coding? ValueCoding { get; set; }
    // R4: QuestionnaireResponse.item.answer.value[x] 0..1 (only one value* is used) - optional
    public Quantity? ValueQuantity { get; set; }
    // R4: QuestionnaireResponse.item.answer.item 0..* - optional
    /// <summary>Child items of a question that has sub-questions (they sit under the answer).</summary>
    public List<QuestionnaireResponseItem> Item { get; set; } = new();
}
