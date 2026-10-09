using Microsoft.AspNetCore.Mvc;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Models.CdsHooks;
using PayerGatewayWebAPI.Models.Responses;
using PayerGatewayWebAPI.Services.Payers;

namespace PayerGatewayWebAPI.Controllers;

/// <summary>STEP 0 - Connected payers: which payerName values the EHR can send, and a connection test (CDS Hooks discovery).</summary>
[ApiController]
[Route("api/v1/payers")]
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

    /// <summary>Lists the configured payers (table PA_PAYER in the EHR database; appsettings "Payers" in memory mode).</summary>
    /// <remarks>Use a payerName from this list in every other request.</remarks>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">The configured payers.</response>
    /// <response code="503">The EHR data API is not available.</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<PayerSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<List<PayerSummary>>> List(CancellationToken ct)
    {
        List<PayerSummary> result = new List<PayerSummary>();
        foreach (PayerSettings payer in await _payers.GetAllAsync(ct))
        {
            PayerSummary summary = new PayerSummary();
            summary.PayerName = payer.PayerName;
            summary.DisplayName = payer.GetDisplayName();
            summary.Enabled = payer.Enabled;
            summary.BaseUrl = payer.BaseUrl;
            summary.PayerIdentifier = payer.PayerIdentifier;
            summary.FhirVersion = payer.IsR5() ? "R5" : "R4";
            summary.DtrMode = payer.UsesSmartApp() ? "SmartApp" : "Forms";
            result.Add(summary);
        }
        return Ok(result);
    }

    /// <summary>Tests a payer connection: GET {payer}/cds-services (CDS Hooks discovery).</summary>
    /// <remarks>
    /// Shows which hooks the payer supports (look for "order-sign") and its prefetch templates,
    /// and refreshes the gateway's 12-hour cache. Use it when a new payer is onboarded.
    /// </remarks>
    /// <param name="payerName">Payer name from GET /api/v1/payers, e.g. MockPayer.</param>
    /// <param name="ct">Cancelled when the caller disconnects.</param>
    /// <response code="200">The payer's CDS services.</response>
    /// <response code="404">The payer is not configured.</response>
    /// <response code="502">The payer answered with an error.</response>
    /// <response code="504">The payer did not answer in time.</response>
    [HttpPost("{payerName}/discover")]
    [ProducesResponseType(typeof(CdsDiscoveryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<CdsDiscoveryResponse>> Discover(string payerName, CancellationToken ct)
    {
        PayerSettings payer = await _payers.GetRequiredAsync(payerName, StatusCodes.Status404NotFound, ct);
        CdsDiscoveryResponse discovery = await _payerClient.DiscoverAsync(payer, ct);
        _payers.CacheDiscovery(payer.PayerName, discovery);
        return Ok(discovery);
    }
}
