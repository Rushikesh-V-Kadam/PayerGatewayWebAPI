using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PayerGatewayWebAPI.Models.Requests;
using PayerGatewayWebAPI.Models.Responses;
using PayerGatewayWebAPI.Services.Cdex;

namespace PayerGatewayWebAPI.Controllers;

/// <summary>
/// STEP 4 - CDex (additional documents for a pended prior auth). What the payer asked for (searched at the payer when
/// the client EHR's job asks with refresh=true) and sending documents / forms. Payers never call the gateway.
/// </summary>
[ApiController]
[Route("cdex")]
[Produces("application/json")]
public class CdexController : ControllerBase
{
    private readonly CdexService _cdex;

    /// <summary>Created by dependency injection.</summary>
    public CdexController(CdexService cdex)
    {
        _cdex = cdex;
    }

    /// <summary>Documents / forms the payer asked for on a pended request.</summary>
    /// <remarks>
    /// Each information request lists LOINC document types (documentsNeeded) and / or questionnaires (questionnairesNeeded).
    /// For questionnaires: fill them in with the payer's SMART DTR app, find the saved form with GET /dtr/saved-forms, then send its questionnaireResponseId.
    /// </remarks>
    /// <param name="requestId">EHR prior auth request id.</param>
    /// <param name="refresh">true = search the payer's Tasks now (the gateway has no background job; the client EHR calls this with refresh=true for each pended request). false = return what is stored.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">The information requests (may be empty).</response>
    /// <response code="404">Unknown requestId.</response>
    /// <response code="502">The payer failed (refresh = true).</response>
    [HttpGet("requests/{requestId}/information-requests")]
    [ProducesResponseType(typeof(List<InformationRequest>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<List<InformationRequest>>> InformationRequests(string requestId, [FromQuery] bool refresh = false, CancellationToken ct = default)
    {
        List<InformationRequest> result = await _cdex.GetInformationRequestsAsync(requestId, refresh, ct);
        return Ok(result);
    }

    /// <summary>Sends additional documents and / or completed forms to the payer ($submit-attachment).</summary>
    /// <remarks>
    /// Use for a pended request (the first documents go with PAS). With final = true the gateway asks the payer for the
    /// new decision right away and returns it in "decision".
    /// </remarks>
    /// <param name="requestId">EHR prior auth request id.</param>
    /// <param name="request">Required. Documents, forms and the tracking id. Send at least one documentId or questionnaireResponseId.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">Sent to the payer.</response>
    /// <response code="400">Nothing to send, or unknown tracking id.</response>
    /// <response code="404">Unknown requestId, document or form.</response>
    /// <response code="422">No member id, no facility NPI and no practitioner NPI, or the payer rejected the attachments (body = result with messages).</response>
    /// <response code="502">The payer or the FHIR API failed, or the file of a document could not be read.</response>
    /// <response code="504">The payer did not answer in time.</response>
    [HttpPost("requests/{requestId}/attachments")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AttachmentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AttachmentResult), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<AttachmentResult>> SubmitAttachments(string requestId, [FromBody, Required] AttachmentSubmitRequest request, CancellationToken ct)
    {
        AttachmentResult result = await _cdex.SubmitAttachmentsAsync(requestId, request, ct);
        return Ok(result);
    }
}
