using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Models.Storage;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.Database;

/// <summary>Payer SMART DTR app links in the EHR database, through the EHR data API: dtr-links/{linkId} (table PA_DTR_LINK).</summary>
public class EhrApiDtrLinkRepository : IDtrLinkRepository
{
    private readonly EhrDataClient _ehr;

    /// <summary>Created by dependency injection.</summary>
    public EhrApiDtrLinkRepository(EhrDataClient ehr)
    {
        _ehr = ehr;
    }

    public async Task SaveAsync(DtrLinkRecord record, CancellationToken ct)
    {
        DtrLinkRow row = new DtrLinkRow();
        row.LinkId = record.LinkId;
        row.PayerName = record.PayerName;
        row.PatientId = record.PatientId;
        row.EncounterId = record.EncounterId;
        row.UserPractitionerId = record.UserPractitionerId;
        row.FhirContextJson = ApiJson.Write(record.FhirContext);
        row.AppContext = record.AppContext;
        row.AppUrl = record.AppUrl;
        row.ExpiresAt = record.ExpiresAt;
        await _ehr.PutAsync("dtr-links/" + EhrDataClient.Esc(record.LinkId), row, ct);
    }

    public async Task<DtrLinkRecord?> GetAsync(string linkId, CancellationToken ct)
    {
        DtrLinkRow? row = await _ehr.GetAsync<DtrLinkRow>("dtr-links/" + EhrDataClient.Esc(linkId), ct);
        if (row == null)
        {
            return null;
        }

        DtrLinkRecord record = new DtrLinkRecord();
        record.LinkId = row.LinkId;
        record.PayerName = row.PayerName;
        record.PatientId = row.PatientId;
        record.EncounterId = row.EncounterId;
        record.UserPractitionerId = row.UserPractitionerId;
        record.FhirContext = ApiJson.Read<List<string>>(row.FhirContextJson);
        record.AppContext = row.AppContext;
        record.AppUrl = row.AppUrl;
        record.ExpiresAt = StorageTime.ToUtc(row.ExpiresAt);
        return record;
    }
}
