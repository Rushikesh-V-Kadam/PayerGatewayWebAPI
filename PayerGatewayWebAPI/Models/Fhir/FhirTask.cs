namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Task (CDex information request, read from the payer), https://hl7.org/fhir/R4/task.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>
/// FHIR Task (PAS / CDex attachment request): the payer asks for documents or forms.
/// Named FhirTask to avoid a clash with System.Threading.Tasks.Task.
/// </summary>
public class FhirTask
{
    public string ResourceType { get; set; } = "Task";
    // R4: Task.id 0..1 - optional
    public string? Id { get; set; }
    // R4: Task.identifier 0..* - optional
    /// <summary>Includes the payer tracking id (type = cdex-temp#tracking-id).</summary>
    public List<Identifier> Identifier { get; set; } = new();
    // R4: Task.status 1..1 - required
    public string? Status { get; set; }
    // R4: Task.code 0..1 - optional
    public CodeableConcept? Code { get; set; }
    // R4: Task.focus 0..1 - optional
    public ResourceReference? Focus { get; set; }
    // R4: Task.reasonReference 0..1 - optional
    /// <summary>Points to our Claim by identifier (the prior auth request id).</summary>
    public ResourceReference? ReasonReference { get; set; }
    // R4: Task.owner 0..1 - optional
    public ResourceReference? Owner { get; set; }
    // R4: Task.restriction 0..1 - optional
    public TaskRestriction? Restriction { get; set; }
    // R4: Task.input 0..* - optional
    public List<TaskInput> Input { get; set; } = new();
}
