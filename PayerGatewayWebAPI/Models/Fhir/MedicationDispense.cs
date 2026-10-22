namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: MedicationRequest.dispenseRequest, https://hl7.org/fhir/R4/medicationrequest.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>MedicationRequest.dispenseRequest (only the quantity is read).</summary>
public class MedicationDispense
{
    // R4: MedicationRequest.dispenseRequest.quantity 0..1 - optional
    public Quantity? Quantity { get; set; }
}
