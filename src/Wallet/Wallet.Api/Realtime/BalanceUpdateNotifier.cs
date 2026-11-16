using Microsoft.AspNetCore.SignalR;
using MiniWallet.Wallet.Application.Features.Transactions;

namespace MiniWallet.Wallet.Api.Realtime;

public sealed record BalanceChanged(Guid TransactionId, string Currency, string BalanceAfter, DateTimeOffset CreatedAt);

public sealed class BalanceUpdateNotifier(IHubContext<BalanceHub> hub, ILogger<BalanceUpdateNotifier> logger)
{
    public async Task PublishAsync(TransactionView transaction)
    {
        // Notification transport must never change the outcome of an already committed operation.
        // Redis does not durably buffer these hints; clients reconcile with the balance REST endpoint.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var message = new BalanceChanged(transaction.TransactionId, transaction.Currency,
                transaction.BalanceAfter, transaction.CreatedAt);
            await hub.Clients.Group(BalanceHub.PlayerGroup(transaction.PlayerId))
                .SendAsync("BalanceChanged", message, timeout.Token).WaitAsync(timeout.Token);
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Live balance notification failed for transaction {TransactionId}; the transaction is committed",
                transaction.TransactionId);
        }
    }
}
