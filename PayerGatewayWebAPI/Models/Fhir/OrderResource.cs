namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: ServiceRequest / MedicationRequest (the orders), https://hl7.org/fhir/R4/resourcelist.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>
/// Typed view of an order: ServiceRequest or MedicationRequest (fields of both in one class, only the ones the
/// gateway reads). The code is in "code" (ServiceRequest) or "medicationCodeableConcept" (MedicationRequest) -
/// use GetCode(). DeviceRequest is not supported: the EHR has no device orders.
/// </summary>
public class OrderResource
{
    /// <summary>FHIR resource type of a service order.</summary>
    public const string ServiceRequestType = "ServiceRequest";

    /// <summary>FHIR resource type of a medication order.</summary>
    public const string MedicationRequestType = "MedicationRequest";

    /// <summary>The order types the gateway reads from the FHIR API and sends to payers.</summary>
    public static readonly string[] SupportedTypes = new string[] { ServiceRequestType, MedicationRequestType };

    public string ResourceType { get; set; } = string.Empty;
    // R4: ServiceRequest / MedicationRequest .id 0..1 - optional
    public string? Id { get; set; }
    // R4: ServiceRequest.subject 1..1 / MedicationRequest.subject 1..1 - required
    public ResourceReference? Subject { get; set; }
    // R4: ServiceRequest.code 0..1 (the claim line needs it: Claim.item.productOrService 1..1) - optional
    /// <summary>What is ordered, on a ServiceRequest.</summary>
    public CodeableConcept? Code { get; set; }
    // R4: MedicationRequest.medication[x] 1..1 - required
    /// <summary>What is ordered, on a MedicationRequest.</summary>
    public CodeableConcept? MedicationCodeableConcept { get; set; }
    // R4: ServiceRequest.quantity[x] 0..1 - optional
    public Quantity? QuantityQuantity { get; set; }
    // R4: ServiceRequest.occurrence[x] 0..1 - optional
    public string? OccurrenceDateTime { get; set; }
    // R4: ServiceRequest.occurrence[x] 0..1 - optional
    public Period? OccurrencePeriod { get; set; }
    // R4: ServiceRequest.requester 0..1 / MedicationRequest.requester 0..1 - optional
    public ResourceReference? Requester { get; set; }
    // R4: ServiceRequest.reasonCode 0..* / MedicationRequest.reasonCode 0..* - optional
    public List<CodeableConcept> ReasonCode { get; set; } = new();
    // CLIENT-EHR: insurance, locationCode, locationReference and an organization performer are not read from orders: the
    // client's orders carry none. The insurance comes as coverageId and the facility as orderLocationCode in the requests.
    // R4: MedicationRequest.dispenseRequest 0..1 - optional
    public MedicationDispense? DispenseRequest { get; set; }

    /// <summary>What is ordered (CPT / HCPCS / RxNorm), whatever the order type.</summary>
    public CodeableConcept? GetCode()
    {
        if (Code != null)
        {
            return Code;
        }
        return MedicationCodeableConcept;
    }

    /// <summary>Requested quantity (dispense quantity for medications).</summary>
    public decimal? GetQuantity()
    {
        if (QuantityQuantity != null && QuantityQuantity.Value != null)
        {
            return QuantityQuantity.Value;
        }
        if (DispenseRequest != null && DispenseRequest.Quantity != null)
        {
            return DispenseRequest.Quantity.Value;
        }
        return null;
    }

    /// <summary>"Type/id" of this order, e.g. "ServiceRequest/ord-1001".</summary>
    public string GetReference()
    {
        return ResourceType + "/" + Id;
    }

    /// <summary>Id of the ordering practitioner, or null.</summary>
    public string? GetRequesterId()
    {
        if (Requester == null)
        {
            return null;
        }
        return Requester.GetId();
    }

    /// <summary>Id of the patient the order is for, or null.</summary>
    public string? GetPatientId()
    {
        if (Subject == null)
        {
            return null;
        }
        return Subject.GetId();
    }
}
