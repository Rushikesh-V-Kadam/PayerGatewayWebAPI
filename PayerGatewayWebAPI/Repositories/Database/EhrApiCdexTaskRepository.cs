using System.Globalization;
using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Models.Storage;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.Database;

/// <summary>
/// Payer information requests (Tasks) in the EHR database (table PA_CDEX_TASK), through the client's own APIs:
///   POST {Ehr:BaseUrl}/{Ehr:DataPath}/savecdxdtl                                        save one Task: insert, or update by InsurancePlanID + TaskID
///   GET  {Ehr:BaseUrl}/{Ehr:DataPath}/getcdxdtl?InsurancePlanID={id}&amp;TaskID={taskId}   one Task ({ "CDXTaskDetail": row or null, ... })
/// CLIENT-EHR: the body uses the client's names (CdexTaskRow). There is no "all Tasks of a request" API: the gateway
/// knows the Task ids of a request from its decision (informationRequests[].taskId).
/// </summary>
public class EhrApiCdexTaskRepository : ICdexTaskRepository
{
    /// <summary>
    /// The client's save API, under Ehr:DataPath.
    /// TODO: the client's path name; update it when the client confirms it.
    /// </summary>
    private const string SavePath = "savecdxdtl";

    /// <summary>
    /// The client's read API (one Task by insurance plan id and Task id), under Ehr:DataPath.
    /// TODO: the client's path name and its parameter names (InsurancePlanID, TaskID) are not confirmed yet.
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

    /// <summary>One Task: GET getcdxdtl?InsurancePlanID={id}&amp;TaskID={taskId}. Null when it does not exist.</summary>
    public async Task<CdexTaskRecord?> GetAsync(int insurancePlanId, string taskId, CancellationToken ct)
    {
        string path = ReadPath
            + "?InsurancePlanID=" + EhrDataClient.Esc(insurancePlanId.ToString(CultureInfo.InvariantCulture))
            + "&TaskID=" + EhrDataClient.Esc(taskId);
        CdexTaskRow? row = await _ehr.GetAsync<CdexTaskRow>(path, ct);
        if (row == null)
        {
            return null;
        }
        return ToRecord(row);
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
