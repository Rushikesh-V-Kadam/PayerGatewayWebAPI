using PayerGatewayWebAPI.Models.Entities;

namespace PayerGatewayWebAPI.Repositories.Interfaces;

/// <summary>Storage for CRD: the latest coverage decision per order.</summary>
public interface ICrdRepository
{
    /// <summary>Latest decision of an order, or null.</summary>
    Task<CoverageDecisionRecord?> GetDecisionAsync(string orderId, CancellationToken ct);

    /// <summary>Inserts or replaces the order's decision.</summary>
    Task SaveDecisionAsync(CoverageDecisionRecord record, CancellationToken ct);
}
