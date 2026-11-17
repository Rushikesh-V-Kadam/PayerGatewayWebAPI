using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Models.Storage;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.Database;

/// <summary>
/// Payer information requests (Tasks) in the EHR database (table PA_CDEX_TASK), through the client's own APIs:
///   POST {Ehr:BaseUrl}/{Ehr:DataPath}/savecdxdtl                                        save one Task: insert, or update by InsurancePlanID + TaskID
///   GET  {Ehr:BaseUrl}/{Ehr:DataPath}/getcdxdtl?RequestID={requestId}                     all Tasks of one request ({ "CDXTaskDetail": [rows], ... })
/// CLIENT-EHR: the body uses the client's names (CdexTaskRow). The read API takes only the request id and answers a
/// list of rows (one per Task id); the caller picks the Task it needs from that list.
/// </summary>
public class EhrApiCdexTaskRepository : ICdexTaskRepository
{
    /// <summary>
    /// The client's save API, under Ehr:DataPath.
    /// TODO: the client's path name; update it when the client confirms it.
    /// </summary>
    private const string SavePath = "savecdxdtl";

    /// <summary>
    /// The client's read API (all Tasks of one request id), under Ehr:DataPath.
    /// TODO: the client's path name and its parameter name (RequestID) are not confirmed yet.
    /// </summary>
    private const string ReadPath = "getcdxdtl";

    private readonly EhrDataClient _ehr;

    /// <summary>Created by dependency injection.</summary>
    public EhrApiCdexTaskRepository(EhrDataClient ehr)
    {
        _ehr = ehr;
    }

    /// <summary>Inserts or updates one Task: POST savecdxdtl with the row in the body.</summary>
    public async Task SaveAsync(CdexTaskRecord record, CancellationToken ct)
    {
        await _ehr.PostAsync(SavePath, ToRow(record), ct);
    }

    /// <summary>
    /// All Tasks of one prior authorization request: GET getcdxdtl?RequestID={requestId}.
    /// The client answers a list of rows (one per Task id). Empty list when the request has no Tasks.
    /// </summary>
    public async Task<List<CdexTaskRecord>> GetByRequestIdAsync(string requestId, CancellationToken ct)
    {
        string path = ReadPath + "?RequestID=" + EhrDataClient.Esc(requestId);
        List<CdexTaskRow> rows = await _ehr.GetListAsync<CdexTaskRow>(path, ct);
        List<CdexTaskRecord> records = new List<CdexTaskRecord>();
        foreach (CdexTaskRow row in rows)
        {
            records.Add(ToRecord(row));
        }
        return records;
    }

    /// <summary>Record -> the client's row.</summary>
    private static CdexTaskRow ToRow(CdexTaskRecord record)
    {
        CdexTaskRow row = new CdexTaskRow();
        row.InsurancePlanId = record.InsurancePlanId;
        row.TaskId = record.TaskId;
        row.RequestId = record.RequestId;
        row.TrackingId = record.TrackingId;
        row.TrackingSystem = record.TrackingSystem;
        row.Status = record.Status;
        row.DueDate = record.DueDate;
        row.TaskJson = record.TaskJson;
        row.ReceivedAt = record.ReceivedAt;
        return row;
    }

    /// <summary>The client's row -> record.</summary>
    private static CdexTaskRecord ToRecord(CdexTaskRow row)
    {
        CdexTaskRecord record = new CdexTaskRecord();
        record.InsurancePlanId = row.InsurancePlanId;
        record.TaskId = row.TaskId;
        record.RequestId = row.RequestId;
        record.TrackingId = row.TrackingId;
        record.TrackingSystem = row.TrackingSystem;
        record.Status = row.Status;
        record.DueDate = row.DueDate;
        record.TaskJson = row.TaskJson;
        record.ReceivedAt = row.ReceivedAt;
        return record;
    }
}
