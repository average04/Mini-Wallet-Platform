namespace MiniWallet.Contracts;

public sealed record PlayerCreatedV1(Guid EventId, Guid PlayerId, string Issuer, string Subject, DateTimeOffset OccurredAt);

