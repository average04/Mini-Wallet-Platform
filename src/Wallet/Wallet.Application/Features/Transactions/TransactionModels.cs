using System.Globalization;
using MiniWallet.Wallet.Domain;
namespace MiniWallet.Wallet.Application.Features.Transactions;

public sealed record TransactionRequest(string Type, string? Amount = null, string? Currency = null,
    Guid? PlayerId = null, Guid? BetId = null, string? Multiplier = null, string? BonusAmount = null);
public sealed record TransactionTarget(Guid PlayerId, Money Money, Guid? BetId = null, WinPayout? Payout = null)
{
    public string Currency => Money.Currency;
}
// Execution metadata is not stored in receipts or exposed in the HTTP response.
public sealed record TransactionExecutionResult(TransactionOutcome Outcome, bool IsReplay);
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
