using PayerGatewayWebAPI.Models.CdsHooks;

namespace PayerGatewayWebAPI.Services.Payers;

/// <summary>A payer's CDS Hooks discovery and when the cached copy expires.</summary>
public class CachedDiscovery
{
    /// <summary>The payer's CDS services and prefetch templates.</summary>
    public CdsDiscoveryResponse Discovery { get; set; } = new CdsDiscoveryResponse();
    /// <summary>The entry is not used after this time (UTC).</summary>
    public DateTime Expires { get; set; }
}
