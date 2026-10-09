using Microsoft.Extensions.Options;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.InMemory;

/// <summary>Payers from appsettings "Payers" (local testing, Storage:Provider = InMemory).</summary>
public class InMemoryPayerRepository : IPayerRepository
{
    private readonly List<PayerSettings> _payers;

    /// <summary>Loads the payer list from appsettings.</summary>
    public InMemoryPayerRepository(IOptions<List<PayerSettings>> payers)
    {
        _payers = payers.Value;
    }

    /// <summary>The payers of appsettings "Payers", as they were read at start-up.</summary>
    public Task<List<PayerSettings>> GetAllAsync(CancellationToken ct)
    {
        return Task.FromResult(_payers);
    }
}
