using MiniWallet.Wallet.Domain;
using MiniWallet.Wallet.Application.Features.Transactions;

namespace MiniWallet.Wallet.Application.Abstractions;

public interface IWalletRepository
{
    Task<BalanceAccount?> LockAsync(Guid playerId, string currency, CancellationToken cancellationToken);
}
public interface ITransactionRepository
{
    Task<LedgerEntry?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> IsSettledAsync(Guid betId, CancellationToken cancellationToken);
    void Add(LedgerEntry entry);
}
public sealed record SavedReceipt(string Fingerprint, TransactionOutcome Outcome);
public interface IIdempotencyRepository
{
    // null reserves a new key; otherwise wait for its writer and return the committed result.
    Task<SavedReceipt?> ReserveAsync(Guid playerId, string key, string fingerprint, CancellationToken cancellationToken);
    Task SaveAsync(Guid playerId, string key, TransactionOutcome result, CancellationToken cancellationToken);
}
