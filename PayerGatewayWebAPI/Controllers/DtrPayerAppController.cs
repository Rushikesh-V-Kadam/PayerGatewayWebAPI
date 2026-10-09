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
/// STEP 2 - DTR, group 2: the payer's own SMART DTR app (PA_PAYER.DTR_MODE = SmartApp). The EHR opens the launch link;
/// the app then works against the FHIR API (launch context, chart, saving the form). The two relay endpoints are only
/// for apps that reach their payer through the gateway.
/// </summary>
[ApiController]
[Route("api/v1/dtr")]
[Tags("DTR 2 - Payer SMART app")]
public class DtrPayerAppController : ControllerBase
{
    private readonly DtrService _dtr;

    /// <summary>Created by dependency injection.</summary>
    public DtrPayerAppController(DtrService dtr)
    {
        _dtr = dtr;
    }

    /// <summary>Opens a payer's SMART DTR app (link from an order-sign message, type "smart").</summary>
    /// <remarks>
    /// Opened in the clinician's browser. Creates the SMART launch at the FHIR API - with the
    /// payer's FHIR release (R4 / R5) - and redirects (302) to the
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

    /// <summary>For payer SMART DTR apps: $questionnaire-package passed to the payer unchanged (body = FHIR Parameters).</summary>
    /// <remarks>
    /// Only needed when a DTR app calls the payer through the gateway (most payer apps call their own payer directly).
    /// </remarks>
    /// <param name="payerName">Payer name.</param>
    /// <param name="body">Required. FHIR Parameters from the DTR app.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">The payer's FHIR answer (the payer's own status code is passed through).</response>
    /// <response code="404">The payer is not configured.</response>
    /// <response code="502">The payer could not be reached.</response>
    /// <response code="504">The payer did not answer in time.</response>
    [HttpPost("{payerName}/Questionnaire/$questionnaire-package")]
    [Consumes(ApiHeaders.FhirJson, ApiHeaders.Json)]
    [Produces(ApiHeaders.FhirJson)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> RelayQuestionnairePackage(string payerName, [FromBody, Required] JsonObject body, CancellationToken ct)
    {
        PayerCallResult result = await _dtr.RelayToPayerAsync(payerName, DtrCodes.QuestionnairePackage, body, ct);
        return FhirResult(result);
    }

    /// <summary>For payer SMART DTR apps: adaptive forms, $next-question passed to the payer unchanged.</summary>
    /// <param name="payerName">Payer name.</param>
    /// <param name="body">Required. FHIR Parameters / QuestionnaireResponse from the DTR app.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">The payer's FHIR answer (the payer's own status code is passed through).</response>
    /// <response code="404">The payer is not configured.</response>
    /// <response code="502">The payer could not be reached.</response>
    /// <response code="504">The payer did not answer in time.</response>
    [HttpPost("{payerName}/Questionnaire/$next-question")]
    [Consumes(ApiHeaders.FhirJson, ApiHeaders.Json)]
    [Produces(ApiHeaders.FhirJson)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> RelayNextQuestion(string payerName, [FromBody, Required] JsonObject body, CancellationToken ct)
    {
        PayerCallResult result = await _dtr.RelayToPayerAsync(payerName, DtrCodes.NextQuestion, body, ct);
        return FhirResult(result);
    }

    /// <summary>Returns the payer's FHIR answer with the payer's status code.</summary>
    private static ContentResult FhirResult(PayerCallResult result)
    {
        ContentResult content = new ContentResult();
        content.Content = result.Body == null ? "{}" : result.Body.ToJsonString();
        content.ContentType = ApiHeaders.FhirJson;
        content.StatusCode = result.StatusCode;
        return content;
    }
}
