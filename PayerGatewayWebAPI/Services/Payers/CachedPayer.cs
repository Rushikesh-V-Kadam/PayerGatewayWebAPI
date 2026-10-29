using PayerGatewayWebAPI.Configuration;

namespace PayerGatewayWebAPI.Services.Payers;

/// <summary>One payer's configuration kept by PayerRegistry, and when it must be read again.</summary>
public class CachedPayer
{
    /// <summary>The payer, with its defaults filled in.</summary>
    public PayerSettings Payer { get; set; } = new PayerSettings();
    /// <summary>The entry is not used after this time (UTC).</summary>
    public DateTime Expires { get; set; }
}
