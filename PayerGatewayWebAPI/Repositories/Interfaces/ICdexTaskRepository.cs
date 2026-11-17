using PayerGatewayWebAPI.Models.Entities;

namespace PayerGatewayWebAPI.Repositories.Interfaces;

/// <summary>Storage for payer information requests (Tasks).</summary>
public interface ICdexTaskRepository
{
    /// <summary>Inserts or updates the Task (key: insurance plan id + Task id).</summary>
    Task SaveAsync(CdexTaskRecord record, CancellationToken ct);

    /// <summary>All Tasks of one prior authorization request (by request id). Empty list when there are none.</summary>
    Task<List<CdexTaskRecord>> GetByRequestIdAsync(string requestId, CancellationToken ct);
}
