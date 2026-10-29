using PayerGatewayWebAPI.Configuration;

namespace PayerGatewayWebAPI.Repositories.Interfaces;

/// <summary>
/// Where one payer's configuration comes from: EhrApiPayerRepository reads it by insurance plan id from the EHR data
/// API (GET payers/{piInsurancePlanId}, table PA_PAYER). There is no payer list: the gateway always asks for one payer.
/// PayerRegistry reads the payer through this interface and keeps it for a few minutes.
/// </summary>
public interface IPayerRepository
{
    /// <summary>The payer of this insurance plan id (enabled or not), as it was read; null when there is no such payer.</summary>
    Task<PayerSettings?> GetByPiInsurancePlanIdAsync(int piInsurancePlanId, CancellationToken ct);
}
