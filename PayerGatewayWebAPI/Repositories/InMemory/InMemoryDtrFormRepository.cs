using System.Collections.Concurrent;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.InMemory;

/// <summary>DTR forms in memory (local testing).</summary>
public class InMemoryDtrFormRepository : IDtrFormRepository
{
    private readonly ConcurrentDictionary<string, DtrFormRecord> _forms = new ConcurrentDictionary<string, DtrFormRecord>();

    /// <summary>Keeps the form and sets UpdatedAt to now.</summary>
    public Task SaveAsync(DtrFormRecord record, CancellationToken ct)
    {
        record.UpdatedAt = DateTime.UtcNow;
        _forms[record.FormId] = record;
        return Task.CompletedTask;
    }

    /// <summary>The form with this id, or null.</summary>
    public Task<DtrFormRecord?> GetAsync(string formId, CancellationToken ct)
    {
        DtrFormRecord? record;
        _forms.TryGetValue(formId, out record);
        return Task.FromResult(record);
    }
}
