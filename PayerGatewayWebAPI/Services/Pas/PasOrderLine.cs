using System.Text.Json.Nodes;
using PayerGatewayWebAPI.Models.Fhir;

namespace PayerGatewayWebAPI.Services.Pas;

/// <summary>One order that becomes one Claim.item: the raw order, its typed view, and whether the line is cancelled.</summary>
public class PasOrderLine
{
    /// <summary>The order as raw FHIR JSON (put into the Bundle unchanged).</summary>
    public JsonObject Raw { get; set; } = new JsonObject();
    /// <summary>The same order as a typed view (code, quantity, dates).</summary>
    public OrderResource Order { get; set; } = new OrderResource();
    /// <summary>true = the line is sent as cancelled (order removed from the request, or the whole request cancelled).</summary>
    public bool Cancelled { get; set; }
}
