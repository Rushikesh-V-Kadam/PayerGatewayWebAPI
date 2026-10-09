using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Models.Storage;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.Database;

/// <summary>
/// Payers from the EHR database: table PA_PAYER, read with GET payers on the EHR data API.
/// The EHR team adds and changes payers in that table; nothing in the gateway changes when a payer is onboarded.
/// </summary>
public class EhrApiPayerRepository : IPayerRepository
{
    private readonly EhrDataClient _ehr;

    /// <summary>Created by dependency injection.</summary>
    public EhrApiPayerRepository(EhrDataClient ehr)
    {
        _ehr = ehr;
    }

    public async Task<List<PayerSettings>> GetAllAsync(CancellationToken ct)
    {
        List<PayerRow> rows = await _ehr.GetListAsync<PayerRow>("payers", ct);
        List<PayerSettings> payers = new List<PayerSettings>();
        foreach (PayerRow row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.PayerName))
            {
                continue;   // a row without a name cannot be used
            }
            payers.Add(ToSettings(row));
        }
        return payers;
    }

    /// <summary>One row -> PayerSettings. Empty columns keep the default value of PayerSettings.</summary>
    private static PayerSettings ToSettings(PayerRow row)
    {
        PayerSettings payer = new PayerSettings();
        payer.PayerName = row.PayerName.Trim();
        payer.DisplayName = row.DisplayName ?? string.Empty;
        payer.Enabled = row.Enabled;
        payer.PayerIdentifier = row.PayerIdentifier ?? string.Empty;
        payer.BaseUrl = row.BaseUrl;
        if (!string.IsNullOrWhiteSpace(row.CdsServicesPath)) payer.CdsServicesPath = row.CdsServicesPath;
        if (!string.IsNullOrWhiteSpace(row.FhirPath)) payer.FhirPath = row.FhirPath;


        payer.UseSubscriptions = row.UseSubscriptions;
        if (!string.IsNullOrWhiteSpace(row.MemberIdSystem)) payer.MemberIdSystem = row.MemberIdSystem;
        if (!string.IsNullOrWhiteSpace(row.PayerIdSystem)) payer.PayerIdSystem = row.PayerIdSystem;
        if (row.CrdTimeoutSeconds != null && row.CrdTimeoutSeconds.Value > 0) payer.CrdTimeoutSeconds = row.CrdTimeoutSeconds.Value;
        if (row.TimeoutSeconds != null && row.TimeoutSeconds.Value > 0) payer.TimeoutSeconds = row.TimeoutSeconds.Value;
        if (!string.IsNullOrWhiteSpace(row.FhirVersion)) payer.FhirVersion = row.FhirVersion.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(row.DtrMode)) payer.DtrMode = row.DtrMode.Trim();
        payer.DtrAppLaunchUrl = string.IsNullOrWhiteSpace(row.DtrAppLaunchUrl) ? null : row.DtrAppLaunchUrl.Trim();
        return payer;
    }
}
