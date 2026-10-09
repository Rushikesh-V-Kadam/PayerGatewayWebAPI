using PayerGatewayWebAPI.Models.Entities;

namespace PayerGatewayWebAPI.Repositories.Interfaces;

/// <summary>Storage for payer information requests (Tasks).</summary>
public interface ICdexTaskRepository
{
    /// <summary>Inserts or updates the Task.</summary>
    Task SaveAsync(CdexTaskRecord record, CancellationToken ct);

    /// <summary>All Tasks of one prior auth request.</summary>
    Task<List<CdexTaskRecord>> GetForRequestAsync(string requestId, CancellationToken ct);
}
