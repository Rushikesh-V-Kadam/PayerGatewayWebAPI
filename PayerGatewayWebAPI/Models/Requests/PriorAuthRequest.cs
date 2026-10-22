using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace PayerGatewayWebAPI.Models.Requests;

/// <summary>
/// Body of POST /api/v1/pas/requests (submit) and PUT /api/v1/pas/requests/{requestId} (update).
/// The gateway turns it into a PAS request Bundle and calls the payer's Claim/$submit.
/// Completed DTR forms for these orders are attached automatically.
/// </summary>
public class PriorAuthRequest
{
    /// <summary>Required for POST. EHR id of the request; becomes Claim.identifier and is used for update, cancel, inquire and CDex. May be empty on PUT (taken from the URL).</summary>
    /// <example>PA-2026-0001</example>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Payer to send the request to, as configured in table PA_PAYER (PAYER_NAME).</summary>
    /// <example>MockPayer</example>
    [Required]
    public string PayerName { get; set; } = string.Empty;

    /// <summary>EHR patient id.</summary>
    /// <example>pat-1</example>
    [Required]
    public string PatientId { get; set; } = string.Empty;

    /// <summary>The patient's active insurance for these orders: the EHR's coverage id (unique in the EHR), read at the FHIR API (GET Coverage/{coverageId}). Required on submit; on an update, empty = keep the insurance of the request.</summary>
    /// <example>cov-1</example>
    // CLIENT-EHR: the EHR sends the active insurance; the orders carry no insurance.
    public string? CoverageId { get; set; }

    /// <summary>
    /// Required. Order location code: the id of the location (facility) in the EHR that asks for the authorization. The
    /// facility, with its own NPI, becomes Claim.provider. When the orders name a facility it must be the same one
    /// (400 otherwise).
    /// </summary>
    /// <example>loc-main</example>
    // CLIENT-EHR: the client's organization has no NPI; the order location code gives the facility NPI.
    // Required by the gateway: the facility is Claim.provider (1..1), so every request names it.
    [Required]
    public string OrderLocationCode { get; set; } = string.Empty;

    /// <summary>
    /// Required. Requesting clinician (Claim.careTeam, role primary). Must exist at the FHIR API (404 otherwise).
    /// The order's ordering practitioner is not used instead.
    /// The client's EHR has no practitioner id and sends the practitioner's NPI here, the same value as userPractitionerId of order-sign.
    /// </summary>
    /// <example>prac-1</example>
    // CLIENT-EHR: the value is the practitioner's NPI (the client's EHR has no practitioner id).
    // Required by the gateway (Claim.careTeam is 0..* in FHIR R4), so every request names its clinician.
    [Required]
    public string RequestingPractitionerId { get; set; } = string.Empty;

    /// <summary>Orders to authorize - one claim line each.</summary>
    /// <example>["ord-1-hd"]</example>
    [Required]
    [MinLength(1, ErrorMessage = "orderIds must contain at least one order id.")]
    public List<string> OrderIds { get; set; } = new();

    /// <summary>routine | urgent (urgent = expedited review, 72 hours under CMS-0057-F).</summary>
    /// <example>routine</example>
    [DefaultValue("routine")]
    public string Priority { get; set; } = "routine";

    /// <summary>new (default) | renewal. Renewal lines are sent with X12 certification type R; previousAuthorizationNumber is sent when given.</summary>
    /// <example>new</example>
    [DefaultValue("new")]
    public string RequestType { get; set; } = "new";

    /// <summary>Optional. Authorization number being renewed (sent as Claim.related, relationship "prior"). Empty = not sent (Claim.related is 0..* in FHIR R4).</summary>
    /// <example>PA20260706123456</example>
    public string? PreviousAuthorizationNumber { get; set; }

    /// <summary>First date of service of the previous authorization (yyyy-MM-dd).</summary>
    /// <example>2026-07-06</example>
    public string? PreviousServiceStart { get; set; }

    /// <summary>Last date of service of the previous authorization (yyyy-MM-dd).</summary>
    /// <example>2026-10-04</example>
    public string? PreviousServiceEnd { get; set; }

    /// <summary>Number of treatments given under the previous authorization.</summary>
    /// <example>39</example>
    public int? PreviousTreatments { get; set; }

    /// <summary>permanent (default) | transient (patient visiting the facility, e.g. while traveling).</summary>
    /// <example>permanent</example>
    [DefaultValue("permanent")]
    public string PatientType { get; set; } = "permanent";

    /// <summary>Physician who referred the patient (sent as a second care team member).</summary>
    /// <example>prac-4</example>
    public string? ReferringPractitionerId { get; set; }

    /// <summary>
    /// Optional, default true. true: completed DTR forms of these orders are found and attached (plus questionnaireResponseIds).
    /// false: no DTR form is read or attached and no DTR call is made; send false when CRD answered callDtr = false.
    /// </summary>
    /// <example>true</example>
    [DefaultValue(true)]
    public bool IncludeDtr { get; set; } = true;

    /// <summary>Extra DTR forms to attach. Completed forms saved for these orders are attached automatically. Not allowed when includeDtr is false.</summary>
    /// <example>[]</example>
    public List<string> QuestionnaireResponseIds { get; set; } = new();

    /// <summary>EHR documents (DocumentReference ids) to attach now; the file content is embedded.</summary>
    /// <example>["doc-1-note"]</example>
    public List<string> DocumentIds { get; set; } = new();

    /// <summary>Free-text note for the reviewer (max 264 characters are sent).</summary>
    /// <example>Patient started hemodialysis on 2026-09-01.</example>
    public string? Notes { get; set; }
}
