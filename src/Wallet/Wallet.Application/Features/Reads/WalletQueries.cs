using System.Globalization;
using System.Text.Json;
using MiniWallet.Wallet.Application.Abstractions;
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
public sealed class WalletQueries(IIdentityRepository identities, IWalletReadRepository reads, ITransactionRepository transactions)
{
    public async Task<Guid> ResolvePlayerAsync(Actor actor, Guid? playerId, CancellationToken ct)
    {
        if (playerId is { } target)
        {
            Permissions.Require(actor, Permissions.ReadAny);
            if (!await reads.ExistsAsync(target, ct)) throw new RequestFailure("player_not_found", FailureKind.NotFound);
            return target;
        }
        if (!actor.Can(Permissions.PlaceBet) && !actor.Can(Permissions.ReadAny))
            throw new RequestFailure("forbidden", FailureKind.Forbidden);
        return await identities.ResolvePlayerAsync(actor.Issuer, actor.Subject, ct)
            ?? throw new RequestFailure("player_not_provisioned", FailureKind.Conflict);
    }
    public async Task<IReadOnlyList<BalanceView>> BalancesAsync(Actor actor, Guid? playerId, CancellationToken ct)
    {
        var target = await ResolvePlayerAsync(actor, playerId, ct);
        var balances = await reads.BalancesAsync(target, ct);
        return balances.Select(x => new BalanceView(x.Currency, x.Balance.ToString("0.########", CultureInfo.InvariantCulture))).ToArray();
    }
    public async Task<HistoryPage> HistoryAsync(Actor actor, Guid? playerId, int? limit, string? cursor, CancellationToken ct)
    {
        var target = await ResolvePlayerAsync(actor, playerId, ct);
        var size = limit ?? 20;
        if (size is < 1 or > 100) throw new RequestFailure("invalid_page_size", FailureKind.Invalid);
        var position = cursor is null ? null : Decode(cursor, target);
        var rows = await reads.HistoryAsync(target, position, size + 1, ct);
        var items = rows.Take(size).Select(TransactionView.From).ToArray();
        var next = rows.Count > size ? Encode(new HistoryPosition(1, target, items[^1].CreatedAt, items[^1].TransactionId)) : null;
        return new HistoryPage(items, next);
    }
    public async Task<TransactionView> TransactionAsync(Actor actor, Guid transactionId, CancellationToken ct)
    {
        var entry = await transactions.FindAsync(transactionId, ct);
        if (entry is null || (!actor.Can(Permissions.ReadAny)
            && (!actor.Can(Permissions.PlaceBet) || await identities.ResolvePlayerAsync(actor.Issuer, actor.Subject, ct) != entry.PlayerId)))
            throw new RequestFailure("transaction_not_found", FailureKind.NotFound);
        return TransactionView.From(entry);
    }
    private static string Encode(HistoryPosition value) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static HistoryPosition Decode(string cursor, Guid playerId)
    {
        try
        {
            if (cursor.Length is 0 or > 1024) throw new FormatException();
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            var value = JsonSerializer.Deserialize<HistoryPosition>(Convert.FromBase64String(base64));
            if (value is null || value.Version != 1 || value.PlayerId != playerId || value.TransactionId == Guid.Empty
                || value.CreatedAt == default || value.CreatedAt.Offset != TimeSpan.Zero) throw new FormatException();
            return value;
        }
        catch (Exception error) when (error is FormatException or JsonException)
        {
            throw new RequestFailure("invalid_cursor", FailureKind.Invalid);
        }
    }
}
