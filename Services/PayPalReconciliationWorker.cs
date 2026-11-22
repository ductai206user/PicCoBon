using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PickleballClub.Logging;
using PickleballClub.Models;

namespace PickleballClub.Services;

public sealed class PayPalReconciliationWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PayPalReconciliationWorker> _logger;

    public PayPalReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PayPalReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            // Brief startup delay before first sweep
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                using (CorrelationContext.BeginWorkerCycle(_logger, nameof(PayPalReconciliationWorker)))
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
                        var payPalService = scope.ServiceProvider.GetRequiredService<PayPalPaymentService>();
                        int reconciled = await payPalService.ReconcileStaleAttemptsAsync(db, cancellationToken: stoppingToken);
                        if (reconciled > 0)
                        {
                            _logger.LogInformation("PayPal reconciliation worker processed {Count} attempt(s)", reconciled);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "PayPal reconciliation worker sweep encountered an error");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }
}
