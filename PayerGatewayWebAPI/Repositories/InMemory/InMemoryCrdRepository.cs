using System.Collections.Concurrent;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.InMemory;

/// <summary>CRD storage in memory (local testing; lost on restart).</summary>
public class InMemoryCrdRepository : ICrdRepository
{
    private readonly ConcurrentDictionary<string, CrdEventRecord> _events = new ConcurrentDictionary<string, CrdEventRecord>();
    private readonly ConcurrentDictionary<string, CoverageDecisionRecord> _decisions = new ConcurrentDictionary<string, CoverageDecisionRecord>();

    /// <summary>The stored answer of an order-sign event, or null.</summary>
    public Task<CrdEventRecord?> GetEventAsync(string eventId, CancellationToken ct)
    {
        CrdEventRecord? record;
        _events.TryGetValue(eventId, out record);
        return Task.FromResult(record);
    }

    /// <summary>Keeps the answer of an order-sign event.</summary>
    public Task SaveEventAsync(CrdEventRecord record, CancellationToken ct)
    {
        _events[record.EventId] = record;
        return Task.CompletedTask;
    }

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
