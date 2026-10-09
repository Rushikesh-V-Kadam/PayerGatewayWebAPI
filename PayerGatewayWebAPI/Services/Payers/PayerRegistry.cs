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
/// When the list is loaded, every payer gets the defaults of its empty optional values (PayerSettings.ApplyDefaults),
/// and a payer that cannot be used (no name, or no valid base URL) is left out with a warning in the log. So the rest
/// of the gateway always works with complete payers, whatever optional columns a row has.
///
/// Also caches each payer's CDS Hooks discovery (services + prefetch templates) for 12 hours.
/// Registered as a singleton.
/// </summary>
public class PayerRegistry
{
    private readonly IPayerRepository _repository;
    private readonly ILogger<PayerRegistry> _logger;
    private readonly int _cacheMinutes;
    private readonly ConcurrentDictionary<string, CachedDiscovery> _discovery = new ConcurrentDictionary<string, CachedDiscovery>();

    /// <summary>The payer list read last, and when it must be read again.</summary>
    private List<PayerSettings> _payers = new List<PayerSettings>();
    private DateTime _payersExpire = DateTime.MinValue;

    /// <summary>Created by dependency injection.</summary>
    public PayerRegistry(IPayerRepository repository, IOptions<StorageSettings> storage, ILogger<PayerRegistry> logger)
    {
        _repository = repository;
        _logger = logger;
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
    public CdsDiscoveryResponse? GetCachedDiscovery(PayerSettings payer)
    {
        CachedDiscovery? cached;
        if (_discovery.TryGetValue(DiscoveryKey(payer), out cached) && cached.Expires > DateTime.UtcNow)
        {
            return cached.Discovery;
        }
        return null;
    }

    /// <summary>
    /// Stores a payer's discovery for 12 hours. A discovery without any service is not kept (and removes the kept
    /// one): the payer is asked again at the next order-sign, so a payer that was not ready yet works as soon as it is.
    /// </summary>
    public void CacheDiscovery(PayerSettings payer, CdsDiscoveryResponse discovery)
    {
        if (discovery.Services == null || discovery.Services.Count == 0)
        {
            CachedDiscovery? removed;
            _discovery.TryRemove(DiscoveryKey(payer), out removed);
            return;
        }

        CachedDiscovery cached = new CachedDiscovery();
        cached.Discovery = discovery;
        cached.Expires = DateTime.UtcNow.AddHours(12);
        _discovery[DiscoveryKey(payer)] = cached;
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
        List<PayerSettings> usable = new List<PayerSettings>();
        foreach (PayerSettings payer in loaded)
        {
            payer.ApplyDefaults();
            string? problem = payer.GetProblem();
            if (problem != null)
            {
                _logger.LogWarning("Payer '{Payer}' is ignored: {Problem}. Correct the row in table PA_PAYER (or the appsettings \"Payers\" entry).",
                    payer.PayerName, problem);
                continue;
            }
            usable.Add(payer);
        }
        _payers = usable;
        _payersExpire = DateTime.UtcNow.AddMinutes(_cacheMinutes);
        return usable;
    }

    /// <summary>
    /// Key of a payer in the discovery cache: its name and its CDS Hooks URL. The URL is part of the key so a payer
    /// whose address was changed in PA_PAYER is asked again right away and does not use the old address's answer.
    /// </summary>
    private static string DiscoveryKey(PayerSettings payer)
    {
        return payer.PayerName.ToLowerInvariant() + "|" + payer.GetCdsServicesUrl();
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
