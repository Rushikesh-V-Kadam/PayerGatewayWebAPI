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

    /// <summary>One Task by insurance plan id and Task id. Null when it does not exist.</summary>
    public Task<CdexTaskRecord?> GetAsync(int insurancePlanId, string taskId, CancellationToken ct)
    {
        CdexTaskRecord? record;
        if (_tasks.TryGetValue(Key(insurancePlanId, taskId), out record))
        {
            return Task.FromResult<CdexTaskRecord?>(record);
        }
        return Task.FromResult<CdexTaskRecord?>(null);
    }

    /// <summary>The dictionary key of a Task.</summary>
    private static string Key(int insurancePlanId, string taskId)
    {
        return insurancePlanId + "|" + taskId;
    }
}
