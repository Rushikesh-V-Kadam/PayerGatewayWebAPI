using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.Database;

/// <summary>
/// Payer information requests (Tasks) in the EHR database, through the EHR data API (table PA_CDEX_TASK):
///   cdex-tasks/{payerName}/{taskId}   save one Task
///   cdex-tasks?requestId=...          all Tasks of one prior auth request
/// The record is sent as it is: every property is one column.
/// </summary>
public class EhrApiCdexTaskRepository : ICdexTaskRepository
{
    private readonly EhrDataClient _ehr;

    /// <summary>Created by dependency injection.</summary>
    public EhrApiCdexTaskRepository(EhrDataClient ehr)
    {
        _ehr = ehr;
    }

    /// <summary>Inserts or updates one Task: PUT cdex-tasks/{payerName}/{taskId}.</summary>
    public async Task SaveAsync(CdexTaskRecord record, CancellationToken ct)
    {
        string path = "cdex-tasks/" + EhrDataClient.Esc(record.PayerName) + "/" + EhrDataClient.Esc(record.TaskId);
        await _ehr.PutAsync(path, record, ct);
    }

    /// <summary>All Tasks of one prior authorization request: GET cdex-tasks?requestId=... (an empty list when there are none).</summary>
    public async Task<List<CdexTaskRecord>> GetForRequestAsync(string requestId, CancellationToken ct)
    {
        return await _ehr.GetListAsync<CdexTaskRecord>("cdex-tasks?requestId=" + EhrDataClient.Esc(requestId), ct);
    }
}
