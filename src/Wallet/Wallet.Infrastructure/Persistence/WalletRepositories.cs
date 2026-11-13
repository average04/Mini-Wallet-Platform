using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MiniWallet.Wallet.Application.Abstractions;
using MiniWallet.Wallet.Application.Features.Transactions;
using MiniWallet.Wallet.Domain;

namespace MiniWallet.Wallet.Infrastructure.Persistence;

public sealed class IdentityRepository(WalletDbContext db) : IIdentityRepository
{
    public Task<Guid?> ResolvePlayerAsync(string issuer, string subject, CancellationToken cancellationToken)
        => db.Identities.Where(x => x.Issuer == issuer && x.Subject == subject).Select(x => (Guid?)x.PlayerId).SingleOrDefaultAsync(cancellationToken);
}
public sealed class WalletRepository(WalletDbContext db) : IWalletRepository
{
    public Task<BalanceAccount?> LockAsync(Guid playerId, string currency, CancellationToken cancellationToken)
        => db.Wallets.FromSqlInterpolated($"SELECT * FROM wallets WHERE player_id = {playerId} AND currency = {currency} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
}
public sealed class TransactionRepository(WalletDbContext db) : ITransactionRepository
{
    public Task<LedgerEntry?> FindAsync(Guid id, CancellationToken cancellationToken)
        => db.Transactions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<bool> IsSettledAsync(Guid betId, CancellationToken cancellationToken)
        => db.Transactions.AnyAsync(x => x.BetId == betId && x.Kind == "WIN", cancellationToken);
    public void Add(LedgerEntry entry) => db.Transactions.Add(entry);
}
public sealed class IdempotencyRepository(WalletDbContext db) : IIdempotencyRepository
{
    private const string Scope = "wallet.transactions.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<SavedReceipt?> ReserveAsync(Guid playerId, string key, string fingerprint, CancellationToken cancellationToken)
    {
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO idempotency (player_id, scope, key, fingerprint, created_at) VALUES ({playerId}, {Scope}, {key}, {fingerprint}, CURRENT_TIMESTAMP) ON CONFLICT DO NOTHING",
            cancellationToken);
        if (inserted == 1) return null;
        var row = await db.Idempotency.AsNoTracking().SingleAsync(x => x.PlayerId == playerId && x.Scope == Scope && x.Key == key, cancellationToken);
        var result = JsonSerializer.Deserialize<TransactionOutcome>(row.ResponseJson ?? throw new InvalidOperationException("Incomplete committed receipt"), Json)
            ?? throw new InvalidOperationException("Invalid committed receipt");
        return new SavedReceipt(row.Fingerprint, result);
    }
    public Task SaveAsync(Guid playerId, string key, TransactionOutcome result, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(result, Json);
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE idempotency SET response_json = CAST({json} AS jsonb) WHERE player_id = {playerId} AND scope = {Scope} AND key = {key}", cancellationToken);
    }
}
public sealed class UnitOfWork(WalletDbContext db) : IUnitOfWork
{
    public async Task BeginAsync(CancellationToken cancellationToken)
        => await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        await db.Database.CommitTransactionAsync(cancellationToken);
    }
    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
            await db.Database.RollbackTransactionAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }
}
