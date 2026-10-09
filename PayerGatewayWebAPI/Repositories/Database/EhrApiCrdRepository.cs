using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.Database;

/// <summary>
/// CRD storage in the EHR database, through the EHR data API:
///   crd-events/{eventId}           table PA_CRD_EVENT
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

    public async Task<CrdEventRecord?> GetEventAsync(string eventId, CancellationToken ct)
    {
        return await _ehr.GetAsync<CrdEventRecord>("crd-events/" + EhrDataClient.Esc(eventId), ct);
    }

    public async Task SaveEventAsync(CrdEventRecord record, CancellationToken ct)
    {
        await _ehr.PutAsync("crd-events/" + EhrDataClient.Esc(record.EventId), record, ct);
    }

    public async Task<CoverageDecisionRecord?> GetDecisionAsync(string orderId, CancellationToken ct)
    {
        return await _ehr.GetAsync<CoverageDecisionRecord>("coverage-decisions/" + EhrDataClient.Esc(orderId), ct);
    }

    public async Task SaveDecisionAsync(CoverageDecisionRecord record, CancellationToken ct)
    {
        record.UpdatedAt = DateTime.UtcNow;
        await _ehr.PutAsync("coverage-decisions/" + EhrDataClient.Esc(record.OrderId), record, ct);
    }
}
