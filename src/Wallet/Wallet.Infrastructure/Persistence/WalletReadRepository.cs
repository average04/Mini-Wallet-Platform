using Microsoft.EntityFrameworkCore;
using MiniWallet.Wallet.Application.Features.Reads;
using MiniWallet.Wallet.Domain;
namespace MiniWallet.Wallet.Infrastructure.Persistence;

public sealed class WalletReadRepository(WalletDbContext db) : IWalletReadRepository
{
    public Task<bool> ExistsAsync(Guid playerId, CancellationToken ct) => db.Wallets.AnyAsync(x => x.PlayerId == playerId, ct);
    public async Task<IReadOnlyList<BalanceAccount>> BalancesAsync(Guid playerId, CancellationToken ct)
        => await db.Wallets.AsNoTracking().Where(x => x.PlayerId == playerId).OrderBy(x => x.Currency).ToListAsync(ct);
    public async Task<IReadOnlyList<LedgerEntry>> HistoryAsync(Guid playerId, HistoryPosition? after, int take, CancellationToken ct)
    {
        var query = after is null
            ? db.Transactions.FromSqlInterpolated($"SELECT * FROM transactions WHERE player_id = {playerId} ORDER BY created_at DESC, id DESC LIMIT {take}")
            : db.Transactions.FromSqlInterpolated($"SELECT * FROM transactions WHERE player_id = {playerId} AND (created_at, id) < ({after.CreatedAt}, {after.TransactionId}) ORDER BY created_at DESC, id DESC LIMIT {take}");
        return await query.AsNoTracking().ToListAsync(ct);
    }
}
