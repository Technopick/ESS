using Ess.Integration.Nav.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ess.Integration.Nav.Queue;

/// <summary>
/// Long-running dispatcher for the outbox (low latency, polls every few seconds). Scheduled NAV → ESS sync,
/// reconciliation sweeps and log retention run as Hangfire recurring jobs configured in the API host.
/// </summary>
public sealed class IntegrationWorker : BackgroundService
{
    private readonly IntegrationQueueProcessor _processor;
    private readonly NavEngineOptions _opt;
    private readonly ILogger<IntegrationWorker> _log;

    public IntegrationWorker(IntegrationQueueProcessor processor, IOptions<NavEngineOptions> opt, ILogger<IntegrationWorker> log)
    {
        _processor = processor;
        _opt = opt.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("NAV integration worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            var handled = 0;
            try
            {
                handled = await _processor.ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _log.LogError(ex, "Integration worker batch failed");
            }
            if (handled == 0)
                await Task.Delay(TimeSpan.FromSeconds(_opt.QueuePollSeconds), stoppingToken).ContinueWith(_ => { });
        }
    }
}
