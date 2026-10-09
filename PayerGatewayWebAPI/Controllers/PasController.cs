using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using PayerGatewayWebAPI.Constants;
using PayerGatewayWebAPI.Models.Requests;
using PayerGatewayWebAPI.Models.Responses;
using PayerGatewayWebAPI.Services.Pas;

namespace PayerGatewayWebAPI.Controllers;

/// <summary>
/// STEP 3 - PAS (prior authorization requests): submit, update, cancel, read, inquire, plus the endpoint the payer
/// calls when a pended decision changes.
/// </summary>
[ApiController]
[Route("api/v1/pas")]
[Produces("application/json")]
public class PasController : ControllerBase
{
    private readonly PasService _pas;

    /// <summary>Created by dependency injection.</summary>
    public PasController(PasService pas)
    {
        _pas = pas;
    }

    /// <summary>Submits a new prior authorization request (Claim/$submit).</summary>
    /// <remarks>
    /// Call when order-sign returned callPas = true (after DTR when callDtr was also true).
    /// The gateway builds the PAS Bundle with the Claim, patient, coverage, organization, practitioner and orders,
    /// attaches **completed DTR forms of these orders automatically**, plus the documents in documentIds (file embedded).
    ///
    /// overallStatus in the answer: approved | partially-approved | denied | pended | not-required | contact-payer.
    /// When pended, the payer may ask for more (informationRequests) → CDex attachments.
    /// Nothing is saved when the payer rejects the request (422 / 502 with the payer's errors); correct the data and
    /// send the same requestId again. The payer can reject with an HTTP error or with an answer that only carries errors
    /// (for example "member not found") - both give 422.
    /// </remarks>
    /// <param name="request">Required. The prior authorization request: requestId, payerName, patientId, requestingOrganizationId and orderIds must be filled.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="201">Accepted by the payer; the decision (may be pended).</response>
    /// <response code="400">Invalid body, missing field, unknown payerName, or an order of another patient.</response>
    /// <response code="404">Patient, order, organization, form or document not found at the FHIR API.</response>
    /// <response code="409">requestId was already submitted (use PUT to change it).</response>
    /// <response code="422">No active insurance (or several and none chosen: send coverageId), insurance without member id, order without code, organization without NPI, or the payer rejected the request (body = decision with errors).</response>
    /// <response code="502">The payer or the FHIR API failed, the payer's answer could not be read, or the file of a document could not be read.</response>
    /// <response code="504">The payer did not answer in time.</response>
    [HttpPost("requests")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PriorAuthDecision), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(PriorAuthDecision), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<PriorAuthDecision>> Submit([FromBody, Required] PriorAuthRequest request, CancellationToken ct)
    {
        PriorAuthDecision decision = await _pas.SubmitAsync(request, false, ct);
        return StatusCode(StatusCodes.Status201Created, decision);
    }

    /// <summary>Changes a request: add / remove orders, add forms or documents (Claim update).</summary>
    /// <remarks>Send the full list of orders that should stay in the request; orders left out are sent as cancelled lines.</remarks>
    /// <param name="requestId">EHR request id, e.g. PA-2026-0001.</param>
    /// <param name="request">Required. The changed request (requestId in the body may be empty or must match the URL).</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">The payer's new decision.</response>
    /// <response code="400">Invalid body, ids do not match, or patient / payer changed.</response>
    /// <response code="404">Unknown requestId, or data not found at the FHIR API.</response>
    /// <response code="409">The request is cancelled.</response>
    /// <response code="422">Insurance without member id, order without code, organization without NPI, or the payer rejected the update (body = decision with errors). The stored request is unchanged.</response>
    /// <response code="502">The payer or the FHIR API failed, the payer's answer could not be read, or the file of a document could not be read.</response>
    /// <response code="504">The payer did not answer in time.</response>
    [HttpPut("requests/{requestId}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PriorAuthDecision), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(PriorAuthDecision), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<PriorAuthDecision>> Update(string requestId, [FromBody, Required] PriorAuthRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(request.RequestId) && request.RequestId != requestId)
        {
            ApiError error = new ApiError("requestId in the body does not match the URL.");
            error.TraceId = HttpContext.TraceIdentifier;
            return BadRequest(error);
        }
        request.RequestId = requestId;
        PriorAuthDecision decision = await _pas.SubmitAsync(request, true, ct);
        return Ok(decision);
    }

    /// <summary>Cancels the whole request (every line sent to the payer as cancelled).</summary>
    /// <param name="requestId">EHR request id.</param>
    /// <param name="cancel">Optional. The reason for cancelling; the body may be left out.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">The cancelled decision.</response>
    /// <response code="404">Unknown requestId.</response>
    /// <response code="422">The request's data can no longer be read, or the payer rejected the cancel (the request stays as it was).</response>
    /// <response code="502">The payer or the FHIR API failed.</response>
    /// <response code="504">The payer did not answer in time.</response>
    [HttpPost("requests/{requestId}/cancel")]
    [ProducesResponseType(typeof(PriorAuthDecision), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<PriorAuthDecision>> Cancel(string requestId, [FromBody] CancelPriorAuthRequest? cancel, CancellationToken ct)
    {
        CancelPriorAuthRequest input = cancel ?? new CancelPriorAuthRequest();
        PriorAuthDecision decision = await _pas.CancelAsync(requestId, input, ct);
        return Ok(decision);
    }

    /// <summary>Latest stored decision (no payer call).</summary>
    /// <param name="requestId">EHR request id.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">The stored decision.</response>
    /// <response code="404">Unknown requestId.</response>
    [HttpGet("requests/{requestId}")]
    [ProducesResponseType(typeof(PriorAuthDecision), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PriorAuthDecision>> Get(string requestId, CancellationToken ct)
    {
        PriorAuthDecision decision = await _pas.GetAsync(requestId, ct);
        return Ok(decision);
    }

    /// <summary>Asks the payer for the latest decision now (Claim/$inquire).</summary>
    /// <remarks>The background job also does this every Workflow:PollMinutes for pended requests.</remarks>
    /// <param name="requestId">EHR request id.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">The latest decision (the stored one when the payer has nothing new, or the request is cancelled).</response>
    /// <response code="404">Unknown requestId.</response>
    /// <response code="502">The payer failed or answered the inquiry with errors only. The stored decision is unchanged.</response>
    /// <response code="504">The payer did not answer in time.</response>
    [HttpPost("requests/{requestId}/inquire")]
    [ProducesResponseType(typeof(PriorAuthDecision), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<PriorAuthDecision>> Inquire(string requestId, CancellationToken ct)
    {
        PriorAuthDecision decision = await _pas.InquireAsync(requestId, ct);
        return Ok(decision);
    }

    /// <summary>Called BY THE PAYER (PAS Subscription rest-hook) when a decision changes.</summary>
    /// <remarks>
    /// The gateway then inquires every open request of that payer and saves the new decisions.
    /// </remarks>
    /// <param name="payerName">Payer name (part of the Subscription endpoint the gateway registered).</param>
    /// <param name="body">Subscription notification Bundle.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">Handled.</response>
    /// <response code="400">The body is not a valid FHIR Bundle.</response>
    /// <response code="404">The payer is not configured.</response>
    [HttpPost("notifications/{payerName}")]
    [Consumes(ApiHeaders.FhirJson, ApiHeaders.Json)]
    [ProducesResponseType(typeof(NotificationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationResult>> Notification(string payerName, [FromBody] JsonObject? body, CancellationToken ct)
    {
        NotificationResult result = await _pas.HandleNotificationAsync(payerName, body, ct);
        return Ok(result);
    }
}
