using Microsoft.Extensions.Options;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Models.Entities;
using PayerGatewayWebAPI.Repositories.Interfaces;
using PayerGatewayWebAPI.Services.Cdex;
using PayerGatewayWebAPI.Services.Pas;

namespace PayerGatewayWebAPI.Background;

/// <summary>
/// Safety net that runs every Workflow:PollMinutes (0 = off). Notifications from payers are the fast path; this job
/// makes sure nothing is missed for requests the payer has pended (a decision can take days):
///   1. look for new payer information requests (CDex Tasks) for pended requests
///   2. ask the payer about every pended request ($inquire)
/// Both steps save what they find in table PA_PRIOR_AUTH, where the EHR reads it.
/// (There is no "retry write-back" step any more: the data is saved straight into the EHR database.)
/// One failure never stops the job: it is logged and the next item / next run continues.
/// </summary>
public class PendingRequestPoller : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly WorkflowSettings _workflow;
    private readonly ILogger<PendingRequestPoller> _logger;

    /// <summary>Created once at startup.</summary>
    public PendingRequestPoller(IServiceScopeFactory scopes, IOptions<WorkflowSettings> workflow, ILogger<PendingRequestPoller> logger)
    {
        _scopes = scopes;
        _workflow = workflow.Value;
        _logger = logger;
    }

    /// <summary>The loop: wait, run once, repeat until the app stops.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_workflow.PollMinutes <= 0)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_workflow.PollMinutes), stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException ex)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;   // the app is stopping
                }
                // a cancelled call that was not caused by the app stopping: the loop must go on
                _logger.LogWarning(ex, "Pending request poll was interrupted; will try again next time");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pending request poll failed; will try again next time");
            }
        }
    }

    /// <summary>One pass over the pended requests.</summary>
    private async Task RunOnceAsync(CancellationToken ct)
    {
        // Services are "scoped", so a new scope is created for each pass
        using (IServiceScope scope = _scopes.CreateScope())
        {
            IPriorAuthRepository repository = scope.ServiceProvider.GetRequiredService<IPriorAuthRepository>();
            PasService pas = scope.ServiceProvider.GetRequiredService<PasService>();
            CdexService cdex = scope.ServiceProvider.GetRequiredService<CdexService>();

            // 1. Tasks: one search per payer + organization
            List<PriorAuthRecord> open = await repository.GetOpenAsync(null, ct);
            List<string> searched = new List<string>();
            foreach (PriorAuthRecord record in open)
            {
                if (record.OrganizationNpi == null)
                {
                    continue;
                }
                string key = record.PayerName.ToLowerInvariant() + "|" + record.OrganizationNpi;
                if (searched.Contains(key))
                {
                    continue;
                }
                searched.Add(key);
                await RunSafelyAsync("Task search at " + record.PayerName, cdex.PollTasksAsync(record.PayerName, record.OrganizationNpi, ct), ct);
            }

            // 2. latest decisions
            foreach (PriorAuthRecord record in open)
            {
                await RunSafelyAsync("Inquiry of " + record.RequestId, pas.InquireAsync(record.RequestId, ct), ct);
            }

            if (open.Count > 0)
            {
                _logger.LogInformation("Poll: {Count} pended request(s) checked", open.Count);
            }
        }
    }

    /// <summary>
    /// Waits for one piece of work; a failure is logged and does not stop the pass.
    /// Only the app stopping (the token is cancelled) is passed on, so the loop can end.
    /// </summary>
    private async Task RunSafelyAsync(string label, Task work, CancellationToken ct)
    {
        try
        {
            await work;
        }
        catch (OperationCanceledException ex)
        {
            if (ct.IsCancellationRequested)
            {
                throw;
            }
            _logger.LogWarning(ex, "{Label} was interrupted", label);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Label} failed", label);
        }
    }
}
