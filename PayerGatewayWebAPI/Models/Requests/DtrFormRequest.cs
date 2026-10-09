using System.ComponentModel.DataAnnotations;

namespace PayerGatewayWebAPI.Models.Requests;

/// <summary>
/// Body of POST /api/v1/dtr/forms: get the payer's questionnaire(s) for these orders as simple questions.
/// Use it when the order-sign decision has callDtr = true (or a CDex information request lists questionnaires).
/// </summary>
public class DtrFormRequest
{
    /// <summary>Payer to ask, as configured in table PA_PAYER (PAYER_NAME).</summary>
    /// <example>MockPayer</example>
    [Required]
    public string PayerName { get; set; } = string.Empty;

    /// <summary>EHR patient id.</summary>
    /// <example>pat-1</example>
    [Required]
    public string PatientId { get; set; } = string.Empty;

    /// <summary>Orders the documentation is for.</summary>
    /// <example>["ord-1-hd"]</example>
    [Required]
    [MinLength(1, ErrorMessage = "orderIds must contain at least one order id.")]
    public List<string> OrderIds { get; set; } = new();

    /// <summary>Insurance to use. Empty = the order's insurance, else the patient's only active insurance. Required when the patient has several active insurances and the order names none.</summary>
    /// <example>cov-1</example>
    public string? CoverageId { get; set; }

    /// <summary>Questionnaire URLs from the order-sign decision (questionnaireUrls). Empty = the payer decides from the orders.</summary>
    /// <example>["http://example.org/fhir/Questionnaire/dialysis-incenter-hd"]</example>
    public List<string> QuestionnaireUrls { get; set; } = new();

    /// <summary>coverageAssertionId from the order-sign decision (DTR "context"), or a CDex tracking id.</summary>
    /// <example>CA-20260929-0001</example>
    public string? CoverageAssertionId { get; set; }
}
