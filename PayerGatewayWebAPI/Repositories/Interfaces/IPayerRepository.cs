using PayerGatewayWebAPI.Configuration;

namespace PayerGatewayWebAPI.Repositories.Interfaces;

/// <summary>
/// Where the payer list comes from. Two implementations, chosen by Storage:Provider:
///   EhrApiPayerRepository    table PA_PAYER in the EHR database, through the EHR data API
///   InMemoryPayerRepository  appsettings "Payers" (local testing)
/// PayerRegistry reads the list through this interface and caches it.
/// </summary>
public interface IPayerRepository
{
    /// <summary>All payers, enabled or not.</summary>
    Task<List<PayerSettings>> GetAllAsync(CancellationToken ct);
}
