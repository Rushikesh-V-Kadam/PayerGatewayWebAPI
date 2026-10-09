using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.Database;

/// <summary>
/// PAS Subscriptions in the EHR database, through the EHR data API:
/// payer-subscriptions/{payerName}/{organizationNpi} (table PA_PAYER_SUBSCRIPTION).
/// The record is sent as it is: every property is one column.
/// </summary>
public class EhrApiPayerSubscriptionRepository : IPayerSubscriptionRepository
{
    private readonly EhrDataClient _ehr;

    /// <summary>Created by dependency injection.</summary>
    public EhrApiPayerSubscriptionRepository(EhrDataClient ehr)
    {
        _ehr = ehr;
    }

    public async Task<PayerSubscriptionRecord?> GetAsync(string payerName, string organizationNpi, CancellationToken ct)
    {
        return await _ehr.GetAsync<PayerSubscriptionRecord>(RowPath(payerName, organizationNpi), ct);
    }

    public async Task SaveAsync(PayerSubscriptionRecord record, CancellationToken ct)
    {
        await _ehr.PutAsync(RowPath(record.PayerName, record.OrganizationNpi), record, ct);
    }

    /// <summary>payer-subscriptions/{payerName}/{organizationNpi}</summary>
    private static string RowPath(string payerName, string organizationNpi)
    {
        return "payer-subscriptions/" + EhrDataClient.Esc(payerName) + "/" + EhrDataClient.Esc(organizationNpi);
    }
}
