using System.Text.Json;
using MediatR;
using MiniWallet.Wallet.Application.Abstractions;
using MiniWallet.Wallet.Application.Features.Transactions;

namespace MiniWallet.Wallet.Application.Features.Reads;

public sealed record GetTransactionHistoryQuery(Actor Actor, Guid? PlayerId = null, int? Limit = null, string? Cursor = null)
    : IRequest<HistoryPage>;

public sealed class GetTransactionHistoryHandler(IIdentityRepository identities, IWalletReadRepository reads)
    : IRequestHandler<GetTransactionHistoryQuery, HistoryPage>
{
    public async Task<HistoryPage> Handle(GetTransactionHistoryQuery query, CancellationToken cancellationToken)
    {
        var target = await WalletAccess.ResolveAsync(query.Actor, query.PlayerId, identities, reads, cancellationToken);
        var size = query.Limit ?? 20;
        if (size is < 1 or > 100) throw new RequestFailure("invalid_page_size", FailureKind.Invalid);
        var position = query.Cursor is null ? null : Decode(query.Cursor, target);
        var rows = await reads.HistoryAsync(target, position, size + 1, cancellationToken);
        var items = rows.Take(size).Select(TransactionView.From).ToArray();
        var next = rows.Count > size ? Encode(new HistoryPosition(1, target, items[^1].CreatedAt, items[^1].TransactionId)) : null;
        return new HistoryPage(items, next);
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
