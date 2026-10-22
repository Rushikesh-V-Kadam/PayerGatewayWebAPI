namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Task.input (each entry), https://hl7.org/fhir/R4/task.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>
/// One Task input: attachments-needed (LOINC), questionnaires-needed (canonical), payer-url ...
/// The claim line it applies to is in an extension (paLineNumber / serviceLineNumber).
/// </summary>
public class TaskInput
{
    // R4: Task.input.extension 0..* - optional
    public List<Extension> Extension { get; set; } = new();
    // R4: Task.input.type 1..1 - required
    public CodeableConcept Type { get; set; } = new();
    // R4: Task.input.value[x] 1..1 (only one value* is used) - required
    public string? ValueString { get; set; }
    // R4: Task.input.value[x] 1..1 (only one value* is used) - required
    public string? ValueUrl { get; set; }
    // R4: Task.input.value[x] 1..1 (only one value* is used) - required
    public string? ValueCanonical { get; set; }
    // R4: Task.input.value[x] 1..1 (only one value* is used) - required
    public Identifier? ValueIdentifier { get; set; }
    // R4: Task.input.value[x] 1..1 (only one value* is used) - required
    public CodeableConcept? ValueCodeableConcept { get; set; }
}
