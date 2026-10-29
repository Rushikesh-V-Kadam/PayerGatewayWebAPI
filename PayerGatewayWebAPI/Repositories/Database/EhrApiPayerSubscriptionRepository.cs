using System.Globalization;
using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.Database;

/// <summary>
/// PAS Subscriptions in the EHR database, through the EHR data API:
/// payer-subscriptions/{insurancePlanId}/{organizationNpi} (table PA_PAYER_SUBSCRIPTION).
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

    /// <summary>The Subscription created at this payer for this organization: GET payer-subscriptions/{insurancePlanId}/{organizationNpi}. Null when there is none.</summary>
    public async Task<PayerSubscriptionRecord?> GetAsync(int insurancePlanId, string organizationNpi, CancellationToken ct)
    {
        return await _ehr.GetAsync<PayerSubscriptionRecord>(RowPath(insurancePlanId, organizationNpi), ct);
    }

    /// <summary>Inserts or updates the Subscription of a payer + organization: PUT payer-subscriptions/{insurancePlanId}/{organizationNpi}.</summary>
    public async Task SaveAsync(PayerSubscriptionRecord record, CancellationToken ct)
    {
        await _ehr.PutAsync(RowPath(record.InsurancePlanId, record.OrganizationNpi), record, ct);
    }

    /// <summary>payer-subscriptions/{insurancePlanId}/{organizationNpi}</summary>
    private static string RowPath(int insurancePlanId, string organizationNpi)
    {
        return "payer-subscriptions/" + insurancePlanId.ToString(CultureInfo.InvariantCulture) + "/" + EhrDataClient.Esc(organizationNpi);
    }
}
