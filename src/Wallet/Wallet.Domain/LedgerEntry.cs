namespace MiniWallet.Wallet.Domain;

// Append-only successful operations. Rejected attempts never enter this ledger.
public sealed class LedgerEntry
{
    public Guid Id { get; init; }
    public Guid PlayerId { get; init; }
    public string Currency { get; init; } = "";
    public string Kind { get; init; } = "";
    public decimal Amount { get; init; }
    public decimal BalanceAfter { get; init; }
    public Guid? BetId { get; init; }
    public string ActorIssuer { get; init; } = "";
    public string ActorSubject { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
}

