using PayerGatewayWebAPI.Models.Entities;

namespace PayerGatewayWebAPI.Repositories.Interfaces;

/// <summary>Storage for prior authorization requests.</summary>
public interface IPriorAuthRepository
{
    /// <summary>The request, or null.</summary>
    Task<PriorAuthRecord?> GetAsync(string requestId, CancellationToken ct);

    /// <summary>Inserts or updates the request.</summary>
    Task SaveAsync(PriorAuthRecord record, CancellationToken ct);

    /// <summary>Requests still pended at a payer (null payerName = all payers).</summary>
    Task<List<PriorAuthRecord>> GetOpenAsync(string? payerName, CancellationToken ct);
}
