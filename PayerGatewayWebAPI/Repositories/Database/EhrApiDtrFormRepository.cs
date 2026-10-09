using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Models.Requests;
using PayerGatewayWebAPI.Models.Storage;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.Database;

/// <summary>DTR forms in the EHR database, through the EHR data API: dtr-forms/{formId} (table PA_DTR_FORM).</summary>
public class EhrApiDtrFormRepository : IDtrFormRepository
{
    private readonly EhrDataClient _ehr;

    /// <summary>Created by dependency injection.</summary>
    public EhrApiDtrFormRepository(EhrDataClient ehr)
    {
        _ehr = ehr;
    }

    /// <summary>Inserts or updates one form: PUT dtr-forms/{formId}. Sets UpdatedAt to now.</summary>
    public async Task SaveAsync(DtrFormRecord record, CancellationToken ct)
    {
        record.UpdatedAt = DateTime.UtcNow;
        await _ehr.PutAsync("dtr-forms/" + EhrDataClient.Esc(record.FormId), ToRow(record), ct);
    }

    /// <summary>One form: GET dtr-forms/{formId}. Null when it does not exist.</summary>
    public async Task<DtrFormRecord?> GetAsync(string formId, CancellationToken ct)
    {
        DtrFormRow? row = await _ehr.GetAsync<DtrFormRow>("dtr-forms/" + EhrDataClient.Esc(formId), ct);
        if (row == null)
        {
            return null;
        }
        return ToRecord(row);
    }

    /// <summary>Record -> row: the lists become JSON text.</summary>
    private static DtrFormRow ToRow(DtrFormRecord record)
    {
        DtrFormRow row = new DtrFormRow();
        row.FormId = record.FormId;
        row.PayerName = record.PayerName;
        row.PatientId = record.PatientId;
        row.CoverageId = record.CoverageId;
        row.OrderIdsJson = ApiJson.Write(record.OrderIds);
        row.QuestionnaireUrl = record.QuestionnaireUrl;
        row.QuestionnaireJson = record.QuestionnaireJson;
        row.Status = record.Status;
        row.QuestionnaireResponseId = record.QuestionnaireResponseId;
        row.AnswersJson = ApiJson.Write(record.Answers);
        row.Adaptive = record.Adaptive;
        row.PayerFinished = record.PayerFinished;
        row.AdaptiveResponseJson = record.AdaptiveResponseJson;
        row.CreatedAt = record.CreatedAt;
        row.UpdatedAt = record.UpdatedAt;
        return row;
    }

    /// <summary>Row -> record: the JSON text becomes lists again.</summary>
    private static DtrFormRecord ToRecord(DtrFormRow row)
    {
        DtrFormRecord record = new DtrFormRecord();
        record.FormId = row.FormId;
        record.PayerName = row.PayerName;
        record.PatientId = row.PatientId;
        record.CoverageId = row.CoverageId;
        record.OrderIds = ApiJson.Read<List<string>>(row.OrderIdsJson);
        record.QuestionnaireUrl = row.QuestionnaireUrl;
        record.QuestionnaireJson = row.QuestionnaireJson;
        record.Status = row.Status;
        record.QuestionnaireResponseId = row.QuestionnaireResponseId;
        record.Answers = ApiJson.Read<List<DtrAnswerInput>>(row.AnswersJson);
        record.Adaptive = row.Adaptive;
        record.PayerFinished = row.PayerFinished;
        record.AdaptiveResponseJson = row.AdaptiveResponseJson;
        record.CreatedAt = StorageTime.ToUtc(row.CreatedAt);
        record.UpdatedAt = StorageTime.ToUtc(row.UpdatedAt);
        return record;
    }
}
