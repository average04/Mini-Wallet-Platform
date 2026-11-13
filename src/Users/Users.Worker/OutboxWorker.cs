using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniWallet.Users.Application.Features.Outbox;
using MiniWallet.Users.Infrastructure.Messaging;
namespace MiniWallet.Users.Worker;

public sealed class OutboxWorker(IServiceScopeFactory scopes, RabbitPublisher publisher, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Connection establishment/topology is outside the bounded claim transaction.
                await publisher.InitializeAsync(stoppingToken);
                await using var scope = scopes.CreateAsyncScope();
                var sent = await scope.ServiceProvider.GetRequiredService<DispatchOutbox>().ExecuteAsync(stoppingToken);
                failures = 0;
                if (!sent) await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                logger.LogWarning("Outbox dispatch failed ({ErrorType}); pending rows will be retried.", error.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(failures++, 5))), stoppingToken);
            }
        }
    }
}
