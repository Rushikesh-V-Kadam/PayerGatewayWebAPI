using System.Collections.Concurrent;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.InMemory;

/// <summary>Tasks in memory (local testing).</summary>
public class InMemoryCdexTaskRepository : ICdexTaskRepository
{
    private readonly ConcurrentDictionary<string, CdexTaskRecord> _tasks = new ConcurrentDictionary<string, CdexTaskRecord>();

    /// <summary>Keeps the Task under insurance plan id + Task id (a second save replaces the first).</summary>
    public Task SaveAsync(CdexTaskRecord record, CancellationToken ct)
    {
        _tasks[Key(record.InsurancePlanId, record.TaskId)] = record;
        return Task.CompletedTask;
    }

    /// <summary>All Tasks of one prior authorization request (by request id). Empty list when there are none.</summary>
    public Task<List<CdexTaskRecord>> GetByRequestIdAsync(string requestId, CancellationToken ct)
    {
        List<CdexTaskRecord> records = new List<CdexTaskRecord>();
        foreach (CdexTaskRecord record in _tasks.Values)
        {
            if (record.RequestId == requestId)
            {
                records.Add(record);
            }
        }
        return Task.FromResult(records);
    }

    /// <summary>The dictionary key of a Task.</summary>
    private static string Key(int insurancePlanId, string taskId)
    {
        return insurancePlanId + "|" + taskId;
    }
}
