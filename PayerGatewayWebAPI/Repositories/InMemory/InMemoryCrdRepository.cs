using System.Collections.Concurrent;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.InMemory;

/// <summary>CRD storage in memory (local testing; lost on restart).</summary>
public class InMemoryCrdRepository : ICrdRepository
{
    private readonly ConcurrentDictionary<string, CoverageDecisionRecord> _decisions = new ConcurrentDictionary<string, CoverageDecisionRecord>();

    /// <summary>The latest coverage decision of an order, or null.</summary>
    public Task<CoverageDecisionRecord?> GetDecisionAsync(string orderId, CancellationToken ct)
    {
        CoverageDecisionRecord? record;
        _decisions.TryGetValue(orderId, out record);
        return Task.FromResult(record);
    }

    /// <summary>Keeps the coverage decision of an order (a new decision replaces the old one).</summary>
    public Task SaveDecisionAsync(CoverageDecisionRecord record, CancellationToken ct)
    {
        _decisions[record.OrderId] = record;
        return Task.CompletedTask;
    }
}
