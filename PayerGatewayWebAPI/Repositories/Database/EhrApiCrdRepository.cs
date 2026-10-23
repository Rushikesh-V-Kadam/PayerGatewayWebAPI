using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.Database;

/// <summary>
/// CRD storage in the EHR database, through the EHR data API:
///   coverage-decisions/{orderId}   table PA_COVERAGE_DECISION
/// The records are sent as they are: every property is one column.
/// </summary>
public class EhrApiCrdRepository : ICrdRepository
{
    private readonly EhrDataClient _ehr;

    /// <summary>Created by dependency injection.</summary>
    public EhrApiCrdRepository(EhrDataClient ehr)
    {
        _ehr = ehr;
    }

    /// <summary>The latest coverage decision of an order: GET coverage-decisions/{orderId}. Null when there is none.</summary>
    public async Task<CoverageDecisionRecord?> GetDecisionAsync(string orderId, CancellationToken ct)
    {
        return await _ehr.GetAsync<CoverageDecisionRecord>("coverage-decisions/" + EhrDataClient.Esc(orderId), ct);
    }

    /// <summary>Saves the coverage decision of an order (one row per order): PUT coverage-decisions/{orderId}. Sets UpdatedAt to now.</summary>
    public async Task SaveDecisionAsync(CoverageDecisionRecord record, CancellationToken ct)
    {
        record.UpdatedAt = DateTime.UtcNow;
        await _ehr.PutAsync("coverage-decisions/" + EhrDataClient.Esc(record.OrderId), record, ct);
    }
}
