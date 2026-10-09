using System.ComponentModel.DataAnnotations;

namespace PayerGatewayWebAPI.Models.Requests;

/// <summary>
/// Body of POST /api/v1/crd/order-sign. Sent by the EHR when the clinician signs one or more orders.
/// The gateway sends the CDS Hooks "order-sign" call to the payer named in payerName.
/// </summary>
public class OrderSignRequest
{
    /// <summary>Unique id of this signing event in the EHR. Sending the same id again returns the stored answer (no second payer call).</summary>
    /// <example>evt-20260929-0001</example>
    [Required]
    public string EventId { get; set; } = string.Empty;

    /// <summary>Payer to ask, exactly as configured in table PA_PAYER (PAYER_NAME, case does not matter).</summary>
    /// <example>MockPayer</example>
    [Required]
    public string PayerName { get; set; } = string.Empty;

    /// <summary>EHR patient id.</summary>
    /// <example>pat-1</example>
    [Required]
    public string PatientId { get; set; } = string.Empty;

    /// <summary>EHR order ids being signed (ServiceRequest, DeviceRequest or MedicationRequest ids at the FHIR API).</summary>
    /// <example>["ord-1-hd"]</example>
    [Required]
    [MinLength(1, ErrorMessage = "orderIds must contain at least one order id.")]
    public List<string> OrderIds { get; set; } = new();

    /// <summary>Insurance to use. Empty = the order's insurance, else the patient's first active coverage.</summary>
    /// <example>cov-1</example>
    public string? CoverageId { get; set; }

    /// <summary>Current encounter, if any (CDS Hooks encounterId).</summary>
    /// <example>enc-1</example>
    public string? EncounterId { get; set; }

    /// <summary>Clinician signing the order (CDS Hooks userId). Empty = the order's requester.</summary>
    /// <example>prac-1</example>
    public string? UserPractitionerId { get; set; }
}
