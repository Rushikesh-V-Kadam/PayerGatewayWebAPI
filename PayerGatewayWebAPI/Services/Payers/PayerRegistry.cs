using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Models.CdsHooks;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Services.Payers;

/// <summary>
/// Finds the payer of a request. The EHR sends insurancePlanId in each request and the gateway reads that one
/// payer's configuration (IPayerRepository: GET payers/{insurancePlanId} on the EHR data API). There is no payer list.
///
/// A payer that was read is kept for Storage:PayerCacheMinutes and then read again, so a change by the EHR team is
/// used after a few minutes without a restart. An insurance plan id without a payer is not kept: it is asked again
/// at the next request, so a payer added by the EHR team works right away.
///
/// When a payer is read, its empty optional values get their defaults (PayerSettings.ApplyDefaults), and a payer that
/// cannot be used (no name, or no valid base URL) is refused with a warning in the log. So the rest of the gateway
/// always works with complete payers, whatever optional columns a row has.
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
    private readonly ConcurrentDictionary<int, CachedPayer> _payers = new ConcurrentDictionary<int, CachedPayer>();

    /// <summary>Created by dependency injection.</summary>
    public PayerRegistry(IPayerRepository repository, IOptions<StorageSettings> storage, ILogger<PayerRegistry> logger)
    {
        _repository = repository;
        _logger = logger;
        _cacheMinutes = Math.Max(0, storage.Value.PayerCacheMinutes);
    }

    /// <summary>
    /// The payer of this insurance plan id, enabled or not, with its defaults filled in. Null when there is no such
    /// payer, or when its configuration cannot be used (the reason is written to the log).
    /// </summary>
    public async Task<PayerSettings?> GetAsync(int insurancePlanId, CancellationToken ct)
    {
        CachedPayer? cached;
        if (_payers.TryGetValue(insurancePlanId, out cached) && cached.Expires > DateTime.UtcNow)
        {
            return cached.Payer;
        }

        PayerSettings? payer = await _repository.GetByInsurancePlanIdAsync(insurancePlanId, ct);
        if (payer == null)
        {
            CachedPayer? removed;
            _payers.TryRemove(insurancePlanId, out removed);
            return null;
        }

        payer.ApplyDefaults();
        string? problem = payer.GetProblem();
        if (problem != null)
        {
            _logger.LogWarning("Payer of insurancePlanId {InsurancePlanId} ('{Payer}') is not used: {Problem}. Correct the row in table PA_PAYER.",
                insurancePlanId, payer.PayerName, problem);
            CachedPayer? removed;
            _payers.TryRemove(insurancePlanId, out removed);
            return null;
        }

        if (_cacheMinutes > 0)
        {
            CachedPayer entry = new CachedPayer();
            entry.Payer = payer;
            entry.Expires = DateTime.UtcNow.AddMinutes(_cacheMinutes);
            _payers[insurancePlanId] = entry;
        }
        return payer;
    }

    /// <summary>The enabled payer of this insurance plan id, or null (no id, no such payer, or switched off).</summary>
    public async Task<PayerSettings?> FindAsync(int? insurancePlanId, CancellationToken ct)
    {
        if (insurancePlanId == null || insurancePlanId.Value <= 0)
        {
            return null;
        }
        PayerSettings? payer = await GetAsync(insurancePlanId.Value, ct);
        if (payer == null || !payer.Enabled)
        {
            return null;
        }
        return payer;
    }

    /// <summary>
    /// The enabled payer of this insurance plan id. Throws ApiException with <paramref name="statusCode"/> when it is
    /// not configured or switched off (400 when the id came in a request body, 404 when it came in the URL,
    /// 422 when it came from a saved row).
    /// </summary>
    public async Task<PayerSettings> GetRequiredAsync(int? insurancePlanId, int statusCode, CancellationToken ct)
    {
        if (insurancePlanId == null || insurancePlanId.Value <= 0)
        {
            throw new ApiException(400, "insurancePlanId is required (a whole number above 0).");
        }
        PayerSettings? payer = await FindAsync(insurancePlanId, ct);
        if (payer == null)
        {
            throw new ApiException(statusCode, "No payer is configured or enabled for insurancePlanId " + insurancePlanId.Value + ".");
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

    /// <summary>
    /// Key of a payer in the discovery cache: its insurance plan id and its CDS Hooks URL. The URL is part of the key
    /// so a payer whose address was changed in PA_PAYER is asked again right away and does not use the old address's answer.
    /// </summary>
    private static string DiscoveryKey(PayerSettings payer)
    {
        return payer.InsurancePlanId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + payer.GetCdsServicesUrl();
    }
}
