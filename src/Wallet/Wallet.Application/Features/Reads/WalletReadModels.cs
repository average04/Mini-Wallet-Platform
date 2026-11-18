using MiniWallet.Wallet.Application.Features.Transactions;
using MiniWallet.Wallet.Domain;

namespace MiniWallet.Wallet.Application.Features.Reads;

public sealed record BalanceView(string Currency, string Balance);
public sealed record HistoryPage(IReadOnlyList<TransactionView> Items, string? NextCursor);
public sealed record HistoryPosition(int Version, Guid PlayerId, DateTimeOffset CreatedAt, Guid TransactionId);
public interface IWalletReadRepository
{
    Task<bool> ExistsAsync(Guid playerId, CancellationToken ct);
    Task<IReadOnlyList<BalanceAccount>> BalancesAsync(Guid playerId, CancellationToken ct);
    Task<IReadOnlyList<LedgerEntry>> HistoryAsync(Guid playerId, HistoryPosition? after, int take, CancellationToken ct);
}
