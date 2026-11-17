using PayerGatewayWebAPI.Models.Entities;

namespace PayerGatewayWebAPI.Repositories.Interfaces;

/// <summary>Storage for payer information requests (Tasks).</summary>
public interface ICdexTaskRepository
{
    /// <summary>Inserts or updates the Task (key: insurance plan id + Task id).</summary>
    Task SaveAsync(CdexTaskRecord record, CancellationToken ct);

    /// <summary>One Task by insurance plan id and Task id. Null when it does not exist.</summary>
    Task<CdexTaskRecord?> GetAsync(int insurancePlanId, string taskId, CancellationToken ct);
}
