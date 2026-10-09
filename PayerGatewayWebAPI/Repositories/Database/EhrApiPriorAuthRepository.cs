using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Constants;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Models.Responses;
using PayerGatewayWebAPI.Models.Storage;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.Database;

/// <summary>
/// Prior auth requests in the EHR database, through the EHR data API (table PA_PRIOR_AUTH):
///   prior-auths/{requestId}                      one request (GET, PUT)
///   prior-auths?status=pended[&amp;payerName=...]    requests still waiting for the payer
/// The EHR reads the decision from the same table, so no separate write-back is needed.
/// </summary>
public class EhrApiPriorAuthRepository : IPriorAuthRepository
{
    private readonly EhrDataClient _ehr;

    /// <summary>Created by dependency injection.</summary>
    public EhrApiPriorAuthRepository(EhrDataClient ehr)
    {
        _ehr = ehr;
    }

    /// <summary>One prior authorization request: GET prior-auths/{requestId}. Null when it does not exist.</summary>
    public async Task<PriorAuthRecord?> GetAsync(string requestId, CancellationToken ct)
    {
        PriorAuthRow? row = await _ehr.GetAsync<PriorAuthRow>("prior-auths/" + EhrDataClient.Esc(requestId), ct);
        if (row == null)
        {
            return null;
        }
        return ToRecord(row);
    }

    /// <summary>Inserts or updates one request: PUT prior-auths/{requestId}. Sets UpdatedAt to now, so call it only when something changed.</summary>
    public async Task SaveAsync(PriorAuthRecord record, CancellationToken ct)
    {
        record.UpdatedAt = DateTime.UtcNow;
        await _ehr.PutAsync("prior-auths/" + EhrDataClient.Esc(record.RequestId), ToRow(record), ct);
    }

    /// <summary>The requests still waiting for a payer decision: GET prior-auths?status=pended, optionally of one payer.</summary>
    public async Task<List<PriorAuthRecord>> GetOpenAsync(string? payerName, CancellationToken ct)
    {
        string path = "prior-auths?status=" + DecisionStatus.Pended;
        if (payerName != null)
        {
            path = path + "&payerName=" + EhrDataClient.Esc(payerName);
        }

        List<PriorAuthRow> rows = await _ehr.GetListAsync<PriorAuthRow>(path, ct);
        List<PriorAuthRecord> records = new List<PriorAuthRecord>();
        foreach (PriorAuthRow row in rows)
        {
            records.Add(ToRecord(row));
        }
        return records;
    }

    /// <summary>Record -> row: lists, the decision and the Bundles become JSON text.</summary>
    private static PriorAuthRow ToRow(PriorAuthRecord record)
    {
        PriorAuthRow row = new PriorAuthRow();
        row.RequestId = record.RequestId;
        row.PayerName = record.PayerName;
        row.PatientId = record.PatientId;
        row.CoverageId = record.CoverageId;
        row.MemberId = record.MemberId;
        row.OrganizationId = record.OrganizationId;
        row.OrganizationNpi = record.OrganizationNpi;
        row.PractitionerId = record.PractitionerId;
        row.PractitionerNpi = record.PractitionerNpi;
        row.Priority = record.Priority;
        row.Status = record.Status;
        row.AuthorizationNumber = record.AuthorizationNumber;
        row.VersionNo = record.Version;
        row.OrderIdsJson = ApiJson.Write(record.OrderIds);
        row.LineNumbersJson = ApiJson.Write(record.LineNumbers);
        row.DecisionJson = ApiJson.Write(record.Decision);
        row.LastRequestBundle = record.LastRequestBundleJson;
        row.LastResponseBundle = record.LastResponseBundleJson;
        row.SubmittedAt = record.SubmittedAt;
        row.UpdatedAt = record.UpdatedAt;
        return row;
    }

    /// <summary>Row -> record: the JSON text becomes lists and the decision again.</summary>
    private static PriorAuthRecord ToRecord(PriorAuthRow row)
    {
        PriorAuthRecord record = new PriorAuthRecord();
        record.RequestId = row.RequestId;
        record.PayerName = row.PayerName;
        record.PatientId = row.PatientId;
        record.CoverageId = row.CoverageId;
        record.MemberId = row.MemberId;
        record.OrganizationId = row.OrganizationId;
        record.OrganizationNpi = row.OrganizationNpi;
        record.PractitionerId = row.PractitionerId;
        record.PractitionerNpi = row.PractitionerNpi;
        record.Priority = row.Priority;
        record.Status = row.Status;
        record.AuthorizationNumber = row.AuthorizationNumber;
        record.Version = row.VersionNo;
        record.OrderIds = ApiJson.Read<List<string>>(row.OrderIdsJson);
        record.LineNumbers = ApiJson.Read<Dictionary<string, int>>(row.LineNumbersJson);
        record.Decision = ApiJson.Read<PriorAuthDecision>(row.DecisionJson);
        record.LastRequestBundleJson = row.LastRequestBundle;
        record.LastResponseBundleJson = row.LastResponseBundle;
        record.SubmittedAt = StorageTime.ToUtc(row.SubmittedAt);
        record.UpdatedAt = StorageTime.ToUtc(row.UpdatedAt);
        return record;
    }
}
