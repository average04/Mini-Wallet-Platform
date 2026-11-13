using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MiniWallet.Wallet.Application.Features.Provisioning;
using MiniWallet.Wallet.Infrastructure.Messaging;
namespace MiniWallet.Wallet.Worker;

public sealed class ProvisioningWorker(IServiceScopeFactory scopes, PlayerEventConsumer consumer) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        consumer.RunAsync(async (message, ct) =>
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ProvisionPlayer>().ExecuteAsync(message, ct);
        }, stoppingToken);
}
