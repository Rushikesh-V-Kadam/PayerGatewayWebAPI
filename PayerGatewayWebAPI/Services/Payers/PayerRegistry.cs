using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Models.CdsHooks;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Services.Payers;

/// <summary>
/// The list of connected payers. The EHR sends payerName in each request and the gateway looks the payer up here
/// by that name (upper / lower case does not matter).
///
/// Where the list comes from (IPayerRepository, chosen by Storage:Provider):
///   EhrApi    table PA_PAYER in the EHR database (GET payers on the EHR data API)
///   InMemory  appsettings "Payers" (local testing)
/// The list is kept for Storage:PayerCacheMinutes and then read again. A name that is not in the kept list is
/// looked up once more right away, so a payer added by the EHR team works without waiting or restarting.
///
/// Also caches each payer's CDS Hooks discovery (services + prefetch templates) for 12 hours.
/// Registered as a singleton.
/// </summary>
public class PayerRegistry
{
    private readonly IPayerRepository _repository;
    private readonly int _cacheMinutes;
    private readonly ConcurrentDictionary<string, CachedDiscovery> _discovery = new ConcurrentDictionary<string, CachedDiscovery>();

    /// <summary>The payer list read last, and when it must be read again.</summary>
    private List<PayerSettings> _payers = new List<PayerSettings>();
    private DateTime _payersExpire = DateTime.MinValue;

    /// <summary>Created by dependency injection.</summary>
    public PayerRegistry(IPayerRepository repository, IOptions<StorageSettings> storage)
    {
        _repository = repository;
        _cacheMinutes = Math.Max(0, storage.Value.PayerCacheMinutes);
    }

    /// <summary>All configured payers (enabled or not).</summary>
    public async Task<List<PayerSettings>> GetAllAsync(CancellationToken ct)
    {
        return await LoadAsync(false, ct);
    }

    /// <summary>The enabled payer with this name, or null.</summary>
    public async Task<PayerSettings?> FindAsync(string? payerName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payerName))
        {
            return null;
        }

        PayerSettings? payer = FindIn(await LoadAsync(false, ct), payerName);
        if (payer == null)
        {
            // Not in the kept list: maybe the payer was added a moment ago. Read the list again once.
            payer = FindIn(await LoadAsync(true, ct), payerName);
        }
        return payer;
    }

    /// <summary>
    /// The enabled payer with this name. Throws ApiException with <paramref name="statusCode"/> when it is not
    /// configured (400 when the name came in a request body, 404 when it came in the URL).
    /// </summary>
    public async Task<PayerSettings> GetRequiredAsync(string? payerName, int statusCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payerName))
        {
            throw new ApiException(400, "payerName is required.");
        }
        PayerSettings? payer = await FindAsync(payerName, ct);
        if (payer == null)
        {
            throw new ApiException(statusCode, "Payer '" + payerName + "' is not configured or not enabled. Configured payers: " + ConfiguredNames() + ".");
        }
        return payer;
    }

    /// <summary>Cached discovery of a payer, or null when not cached / expired.</summary>
    public CdsDiscoveryResponse? GetCachedDiscovery(string payerName)
    {
        CachedDiscovery? cached;
        if (_discovery.TryGetValue(payerName.ToLowerInvariant(), out cached) && cached.Expires > DateTime.UtcNow)
        {
            return cached.Discovery;
        }
        return null;
    }

    /// <summary>Stores a payer's discovery for 12 hours.</summary>
    public void CacheDiscovery(string payerName, CdsDiscoveryResponse discovery)
    {
        CachedDiscovery cached = new CachedDiscovery();
        cached.Discovery = discovery;
        cached.Expires = DateTime.UtcNow.AddHours(12);
        _discovery[payerName.ToLowerInvariant()] = cached;
    }

    // ================================================================== helpers

    /// <summary>The kept payer list; reads it again when it is too old or <paramref name="force"/> is true.</summary>
    private async Task<List<PayerSettings>> LoadAsync(bool force, CancellationToken ct)
    {
        if (!force && DateTime.UtcNow < _payersExpire)
        {
            return _payers;
        }
        List<PayerSettings> loaded = await _repository.GetAllAsync(ct);
        _payers = loaded;
        _payersExpire = DateTime.UtcNow.AddMinutes(_cacheMinutes);
        return loaded;
    }

    /// <summary>The enabled payer with this name in a list, or null.</summary>
    private static PayerSettings? FindIn(List<PayerSettings> payers, string payerName)
    {
        foreach (PayerSettings payer in payers)
        {
            if (payer.Enabled && string.Equals(payer.PayerName, payerName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return payer;
            }
        }
        return null;
    }

    /// <summary>"A, B, C" list of enabled payer names, for error messages.</summary>
    private string ConfiguredNames()
    {
        List<string> names = new List<string>();
        foreach (PayerSettings payer in _payers)
        {
            if (payer.Enabled)
            {
                names.Add(payer.PayerName);
            }
        }
        if (names.Count == 0)
        {
            return "(none)";
        }
        return string.Join(", ", names);
    }
}
