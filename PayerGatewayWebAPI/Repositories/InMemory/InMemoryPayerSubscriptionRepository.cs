using System.Collections.Concurrent;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.InMemory;

/// <summary>Subscriptions in memory (local testing).</summary>
public class InMemoryPayerSubscriptionRepository : IPayerSubscriptionRepository
{
    private readonly ConcurrentDictionary<string, PayerSubscriptionRecord> _subscriptions = new ConcurrentDictionary<string, PayerSubscriptionRecord>();

    /// <summary>The Subscription of this payer + organization, or null (upper / lower case of the payer name does not matter).</summary>
    public Task<PayerSubscriptionRecord?> GetAsync(string payerName, string organizationNpi, CancellationToken ct)
    {
        PayerSubscriptionRecord? record;
        _subscriptions.TryGetValue(payerName.ToLowerInvariant() + "|" + organizationNpi, out record);
        return Task.FromResult(record);
    }

    /// <summary>Keeps the Subscription of a payer + organization.</summary>
    public Task SaveAsync(PayerSubscriptionRecord record, CancellationToken ct)
    {
        _subscriptions[record.PayerName.ToLowerInvariant() + "|" + record.OrganizationNpi] = record;
        return Task.CompletedTask;
    }
}
