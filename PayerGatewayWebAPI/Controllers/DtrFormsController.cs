using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using PayerGatewayWebAPI.Constants;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Models.Requests;
using PayerGatewayWebAPI.Models.Responses;
using PayerGatewayWebAPI.Services.Dtr;
using PayerGatewayWebAPI.Services.Payers;

namespace PayerGatewayWebAPI.Controllers;

/// <summary>
/// STEP 2 - DTR, group 1: our forms (PA_PAYER.DTR_MODE = Forms). For payers without their own questionnaire app.
/// The gateway gets the payer's questionnaire and returns it as simple questions; our questionnaire app (demo) or an
/// EHR screen shows them and sends the answers back. saved-forms is used by both groups.
/// </summary>
[ApiController]
[Route("api/v1/dtr")]
[Tags("DTR 1 - Forms (our questionnaire app or EHR screen)")]
public class DtrFormsController : ControllerBase
{
    private readonly DtrService _dtr;

    /// <summary>Created by dependency injection.</summary>
    public DtrFormsController(DtrService dtr)
    {
        _dtr = dtr;
    }


    /// <summary>Gets the payer's questionnaire(s) for the orders, as simple questions.</summary>
    /// <remarks>
    /// Call when order-sign returned callDtr = true. The gateway calls the payer's Questionnaire/$questionnaire-package
    /// with the orders and coverage, and returns one form per questionnaire.
    ///
    /// Next: show each question (text, type, options, required, showWhen) on the EHR screen and send the answers to
    /// POST /api/v1/dtr/forms/{formId}/answers. During testing the answers can be sent from Swagger or Postman.
    /// </remarks>
    /// <param name="request">Required. Payer, patient, orders and the questionnaire URLs from the order-sign decision.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">The forms with their questions.</response>
    /// <response code="400">Invalid body, missing field, unknown payerName, or an order of another patient.</response>
    /// <response code="404">An order was not found at the FHIR API.</response>
    /// <response code="422">No active insurance (or several and none chosen: send coverageId), or the payer refused / returned no questionnaire.</response>
    /// <response code="502">The payer or the FHIR API failed.</response>
    /// <response code="504">The payer did not answer in time.</response>
    [HttpPost("forms")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<DtrForm>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<List<DtrForm>>> CreateForms([FromBody, Required] DtrFormRequest request, CancellationToken ct)
    {
        List<DtrForm> forms = await _dtr.CreateFormsAsync(request, ct);
        return Ok(forms);
    }

    /// <summary>Reads a form: its questions and the answers saved so far.</summary>
    /// <param name="formId">Form id from POST /api/v1/dtr/forms, e.g. frm-6f1c2a9e.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">The form.</response>
    /// <response code="404">Unknown formId.</response>
    [HttpGet("forms/{formId}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(DtrForm), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DtrForm>> GetForm(string formId, CancellationToken ct)
    {
        DtrForm form = await _dtr.GetFormAsync(formId, ct);
        return Ok(form);
    }

    /// <summary>Saves the answers of a form (builds and stores the FHIR QuestionnaireResponse).</summary>
    /// <remarks>
    /// Send one entry per question: { "linkId": "...", "values": ["..."] }. Values are text in the format of the question type
    /// (boolean "true", integer "12", date "2026-08-10", choice = the option code, quantity "12 mL/min").
    ///
    /// - Answers are merged with earlier ones; an entry with an empty values list removes that answer.
    /// - complete = true: all required (shown) questions must be answered, otherwise 422 lists them.
    /// - complete = false: saved as in-progress; missingRequired lists what is still open.
    ///
    /// The QuestionnaireResponse is saved at the FHIR API. POST /api/v1/pas/requests attaches completed forms
    /// of the same orders automatically; for CDex send questionnaireResponseId in the attachments call.
    /// </remarks>
    /// <param name="formId">Form id from POST /api/v1/dtr/forms.</param>
    /// <param name="request">Required. The answers (one entry per answered question).</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">Saved; the QuestionnaireResponse id is returned.</response>
    /// <response code="400">An answer has the wrong format or is not one of the options (details list each one). Nothing saved.</response>
    /// <response code="404">Unknown formId.</response>
    /// <response code="422">complete = true but required questions are unanswered (details list them). Nothing saved.</response>
    /// <response code="502">The FHIR API failed to save the form.</response>
    [HttpPost("forms/{formId}/answers")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(DtrAnswersResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<DtrAnswersResult>> SaveAnswers(string formId, [FromBody, Required] DtrAnswersRequest request, CancellationToken ct)
    {
        DtrAnswersResult result = await _dtr.SaveAnswersAsync(formId, request, ct);
        return Ok(result);
    }

    /// <summary>Lists the DTR forms saved for a patient (by the gateway forms or by a payer's SMART DTR app).</summary>
    /// <remarks>
    /// After the payer's own DTR app was used (it saves its QuestionnaireResponse at the FHIR API), call this to see
    /// whether the form is completed. POST /api/v1/pas/requests attaches completed forms of the same orders automatically.
    /// </remarks>
    /// <param name="patientId">Patient id, e.g. pat-1. Required.</param>
    /// <param name="orderId">Optional: only the forms of this order.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">The saved forms, newest first (empty list when none).</response>
    /// <response code="400">patientId missing.</response>
    /// <response code="502">The FHIR API failed.</response>
    [HttpGet("saved-forms")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<SavedForm>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<List<SavedForm>>> GetSavedForms([FromQuery] string? patientId, [FromQuery] string? orderId, CancellationToken ct)
    {
        List<SavedForm> forms = await _dtr.GetSavedFormsAsync(patientId, orderId, ct);
        return Ok(forms);
    }
}
