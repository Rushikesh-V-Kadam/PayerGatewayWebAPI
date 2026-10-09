using Microsoft.AspNetCore.Mvc;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Models.Responses;

namespace PayerGatewayWebAPI.Controllers;

/// <summary>Public endpoints: a health check, and "/" -> Swagger.</summary>
[ApiController]
public class WellKnownController : ControllerBase
{
    /// <summary>Simple "is the gateway running" check.</summary>
    /// <response code="200">The gateway is running.</response>
    [HttpGet("/health")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(HealthStatus), StatusCodes.Status200OK)]
    public ActionResult<HealthStatus> Health()
    {
        HealthStatus status = new HealthStatus();
        status.Status = "ok";
        status.Time = FhirDate.Now();
        return Ok(status);
    }

    /// <summary>Opens Swagger UI when the root URL is browsed.</summary>
    [HttpGet("/")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Root()
    {
        return Redirect("/swagger");
    }
}
