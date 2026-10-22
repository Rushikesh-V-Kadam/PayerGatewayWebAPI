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

    /// <summary>EHR order ids being signed: service orders and medication orders (ServiceRequest or MedicationRequest ids at the FHIR API).</summary>
    /// <example>["ord-1-hd"]</example>
    [Required]
    [MinLength(1, ErrorMessage = "orderIds must contain at least one order id.")]
    public List<string> OrderIds { get; set; } = new();

    /// <summary>Required. The patient's active insurance for these orders: the EHR's coverage id (unique in the EHR). The gateway reads it at the FHIR API (GET Coverage/{coverageId}); the orders carry no insurance.</summary>
    /// <example>cov-1</example>
    // CLIENT-EHR: the EHR sends the active insurance; the gateway no longer picks one from the patient's insurances.
    [Required]
    public string CoverageId { get; set; } = string.Empty;

    /// <summary>Current encounter, if any (CDS Hooks encounterId).</summary>
    /// <example>enc-1</example>
    public string? EncounterId { get; set; }

    /// <summary>
    /// Required. Clinician signing the order (CDS Hooks context.userId, 1..1). The order's ordering practitioner is not used instead.
    /// The client's EHR has no practitioner id and sends the practitioner's NPI here; the FHIR API reads the practitioner by it.
    /// </summary>
    /// <example>prac-1</example>
    // CLIENT-EHR: the value is the practitioner's NPI (the client's EHR has no practitioner id).
    [Required]
    public string UserPractitionerId { get; set; } = string.Empty;
}
