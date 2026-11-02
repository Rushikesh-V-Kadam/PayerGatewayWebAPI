using Microsoft.AspNetCore.Mvc;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Models.CdsHooks;
using PayerGatewayWebAPI.Models.Responses;
using PayerGatewayWebAPI.Services.Payers;

namespace PayerGatewayWebAPI.Controllers;

/// <summary>STEP 0 - Payer configuration: the payer of an insurance plan id, and a connection test (CDS Hooks discovery).</summary>
[ApiController]
[Route("payers")]
[Produces("application/json")]
public class PayersController : ControllerBase
{
    private readonly PayerRegistry _payers;
    private readonly PayerClient _payerClient;

    /// <summary>Created by dependency injection.</summary>
    public PayersController(PayerRegistry payers, PayerClient payerClient)
    {
        _payers = payers;
        _payerClient = payerClient;
    }

    /// <summary>Shows the payer configuration of one insurance plan id.</summary>
    /// <remarks>
    /// Read from the EHR data API (GET payers/{insurancePlanId}, table PA_PAYER). A switched-off payer is shown too
    /// (enabled = false); the other requests refuse it.
    /// </remarks>
    /// <param name="insurancePlanId">The EHR's insurance plan id, e.g. 1001.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">The payer configuration.</response>
    /// <response code="404">No usable payer configuration for this insurance plan id.</response>
    /// <response code="503">The EHR data API is not available.</response>
    [HttpGet("{insurancePlanId:int}")]
    [ProducesResponseType(typeof(PayerSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PayerSummary>> Get(int insurancePlanId, CancellationToken ct)
    {
        PayerSettings? payer = await _payers.GetAsync(insurancePlanId, ct);
        if (payer == null)
        {
            throw new ApiException(StatusCodes.Status404NotFound, "No usable payer configuration for insurancePlanId " + insurancePlanId + ".");
        }

        PayerSummary summary = new PayerSummary();
        summary.InsurancePlanId = payer.InsurancePlanId;
        summary.PayerName = payer.PayerName;
        summary.Enabled = payer.Enabled;
        summary.BaseUrl = payer.BaseUrl;
        return Ok(summary);
    }

    /// <summary>Tests a payer connection: GET {payer}/cds-services (CDS Hooks discovery).</summary>
    /// <remarks>
    /// Shows which hooks the payer supports (look for "order-sign") and its prefetch templates,
    /// and refreshes the gateway's 12-hour cache. Use it when a new payer is onboarded.
    /// </remarks>
    /// <param name="insurancePlanId">The EHR's insurance plan id of the payer, e.g. 1001.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">The payer's CDS services.</response>
    /// <response code="404">No payer is configured or enabled for this insurance plan id.</response>
    /// <response code="502">The payer answered with an error.</response>
    /// <response code="504">The payer did not answer in time.</response>
    [HttpPost("{insurancePlanId:int}/discover")]
    [ProducesResponseType(typeof(CdsDiscoveryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<CdsDiscoveryResponse>> Discover(int insurancePlanId, CancellationToken ct)
    {
        PayerSettings payer = await _payers.GetRequiredAsync(insurancePlanId, StatusCodes.Status404NotFound, ct);
        CdsDiscoveryResponse discovery = await _payerClient.DiscoverAsync(payer, payer.TimeoutSeconds, ct);
        _payers.CacheDiscovery(payer, discovery);
        return Ok(discovery);
    }
}
