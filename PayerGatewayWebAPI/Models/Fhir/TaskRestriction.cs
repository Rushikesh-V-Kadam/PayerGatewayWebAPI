namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Task.restriction, https://hl7.org/fhir/R4/task.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>Task.restriction: the due date is in period.end.</summary>
public class TaskRestriction
{
    // R4: Task.restriction.period 0..1 - optional
    public Period? Period { get; set; }
}
