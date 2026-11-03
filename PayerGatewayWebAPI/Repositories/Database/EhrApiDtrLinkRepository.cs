using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Models.Storage;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.Database;

/// <summary>Payer SMART DTR app links in the EHR database, through the EHR data API: POST dtr-links, GET dtr-links/{linkId} (table PA_DTR_LINK).</summary>
public class EhrApiDtrLinkRepository : IDtrLinkRepository
{
    private readonly EhrDataClient _ehr;

    /// <summary>Created by dependency injection.</summary>
    public EhrApiDtrLinkRepository(EhrDataClient ehr)
    {
        _ehr = ehr;
    }

    /// <summary>Inserts one new DTR app link: POST dtr-links (linkId, created by the gateway, is in the body). The list of references is sent as JSON text.</summary>
    public async Task SaveAsync(DtrLinkRecord record, CancellationToken ct)
    {
        DtrLinkRow row = new DtrLinkRow();
        row.LinkId = record.LinkId;
        row.InsurancePlanId = record.InsurancePlanId;
        row.PatientId = record.PatientId;
        row.EncounterId = record.EncounterId;
        row.UserPractitionerId = record.UserPractitionerId;
        row.FhirContextJson = ApiJson.Write(record.FhirContext);
        row.AppContext = record.AppContext;
        row.AppUrl = record.AppUrl;
        row.ExpiresAt = record.ExpiresAt;
        await _ehr.PostAsync("dtr-links", row, ct);
    }

    /// <summary>One DTR app link: GET dtr-links/{linkId}. Null when it does not exist.</summary>
    public async Task<DtrLinkRecord?> GetAsync(string linkId, CancellationToken ct)
    {
        DtrLinkRow? row = await _ehr.GetAsync<DtrLinkRow>("dtr-links/" + EhrDataClient.Esc(linkId), ct);
        if (row == null)
        {
            return null;
        }

        DtrLinkRecord record = new DtrLinkRecord();
        record.LinkId = row.LinkId;
        record.InsurancePlanId = row.InsurancePlanId;
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
