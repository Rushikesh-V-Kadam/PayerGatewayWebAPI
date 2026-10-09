namespace PayerGatewayWebAPI.Models.Fhir;

/// <summary>MedicationRequest.dispenseRequest (only the quantity is read).</summary>
public class MedicationDispense
{
    public Quantity? Quantity { get; set; }
}
