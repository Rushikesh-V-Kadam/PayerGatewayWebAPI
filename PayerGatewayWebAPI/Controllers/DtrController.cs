using System.Net;
using Microsoft.AspNetCore.Mvc;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Models.Responses;
using PayerGatewayWebAPI.Services.Dtr;

namespace PayerGatewayWebAPI.Controllers;

/// <summary>
/// STEP 2 - DTR (payer questionnaires), filled in with the payer's SMART app.
///   GET  /dtr/launch/{linkId}   the EHR opens this link from order-sign; it starts the payer's app
///   GET  /dtr/saved-forms       the forms the app saved, so the EHR can see the questionnaire is done
/// The payer's app gets its questionnaires from its own payer; it does not call the gateway.
/// </summary>
[ApiController]
[Route("dtr")]
[Tags("DTR - Payer SMART app")]
public class DtrController : ControllerBase
{
    private readonly DtrService _dtr;

    /// <summary>Created by dependency injection.</summary>
    public DtrController(DtrService dtr)
    {
        _dtr = dtr;
    }

    /// <summary>Opens a payer's SMART DTR app (link from an order-sign message, type "smart").</summary>
    /// <remarks>
    /// Opened in the clinician's browser. Creates the SMART launch at the FHIR API (FHIR R4) and redirects (302) to the
    /// payer's DTR app with iss and launch. Shows a short HTML page when the link is invalid or expired.
    /// </remarks>
    /// <param name="linkId">Id at the end of the link.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="302">Redirect to the payer DTR app.</response>
    /// <response code="404">Unknown or expired link (HTML page).</response>
    [HttpGet("launch/{linkId}")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Launch(string linkId, CancellationToken ct)
    {
        try
        {
            string url = await _dtr.LaunchAsync(linkId, ct);
            return Redirect(url);
        }
        catch (ApiException ex)
        {
            string html = "<!doctype html><html><body style=\"font-family:sans-serif;margin:2rem\"><h2>Documentation link unavailable</h2>" +
                          "<p>" + WebUtility.HtmlEncode(ex.Message) + "</p></body></html>";
            ContentResult page = new ContentResult();
            page.Content = html;
            page.ContentType = "text/html";
            page.StatusCode = ex.StatusCode;
            return page;
        }
    }

    /// <summary>Lists the DTR forms a payer's SMART DTR app saved for a patient.</summary>
    /// <remarks>
    /// After the payer's DTR app was used (it saves its QuestionnaireResponse at the FHIR API), call this to see
    /// whether the form is completed. POST /pas/requests attaches completed forms of the same orders automatically.
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
