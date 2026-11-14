using System.Globalization;
using MiniWallet.Wallet.Domain;
namespace MiniWallet.Wallet.Application.Features.Transactions;

public sealed record TransactionRequest(string Type, string? Amount, string? Currency = null, Guid? PlayerId = null, Guid? BetId = null);
public sealed record TransactionTarget(Guid PlayerId, string Currency, Guid? BetId = null);
public sealed record TransactionOutcome(TransactionView? Transaction, string? Code)
{
    public static TransactionOutcome Success(LedgerEntry entry) => new(TransactionView.From(entry), null);
    public static TransactionOutcome Rejected(string code) => new(null, code);
}
public sealed record TransactionView(Guid TransactionId, Guid PlayerId, string Type, string Currency, string Amount,
    string BalanceAfter, Guid? BetId, DateTimeOffset CreatedAt)
{
    public static TransactionView From(LedgerEntry entry) => new(entry.Id, entry.PlayerId, entry.Kind, entry.Currency,
        entry.Amount.ToString("0.########", CultureInfo.InvariantCulture), entry.BalanceAfter.ToString("0.########", CultureInfo.InvariantCulture),
        entry.BetId, entry.CreatedAt);
}
