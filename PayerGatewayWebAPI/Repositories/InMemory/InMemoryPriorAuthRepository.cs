using System.Collections.Concurrent;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.InMemory;

/// <summary>Prior auth requests in memory (local testing).</summary>
public class InMemoryPriorAuthRepository : IPriorAuthRepository
{
    private readonly ConcurrentDictionary<string, PriorAuthRecord> _requests = new ConcurrentDictionary<string, PriorAuthRecord>();

    public Task<PriorAuthRecord?> GetAsync(string requestId, CancellationToken ct)
    {
        PriorAuthRecord? record;
        _requests.TryGetValue(requestId, out record);
        return Task.FromResult(record);
    }

    public Task SaveAsync(PriorAuthRecord record, CancellationToken ct)
    {
        record.UpdatedAt = DateTime.UtcNow;
        _requests[record.RequestId] = record;
        return Task.CompletedTask;
    }

    public Task<List<PriorAuthRecord>> GetOpenAsync(string? payerName, CancellationToken ct)
    {
        List<PriorAuthRecord> open = new List<PriorAuthRecord>();
        foreach (PriorAuthRecord record in _requests.Values)
        {
            bool payerMatches = payerName == null || string.Equals(record.PayerName, payerName, StringComparison.OrdinalIgnoreCase);
            if (record.IsOpen() && payerMatches)
            {
                open.Add(record);
            }
        }
        return Task.FromResult(open);
    }
}
