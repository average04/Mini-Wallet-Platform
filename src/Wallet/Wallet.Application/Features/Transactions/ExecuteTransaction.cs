using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MiniWallet.Wallet.Application.Abstractions;
using MiniWallet.Wallet.Domain;

namespace MiniWallet.Wallet.Application.Features.Transactions;

public sealed class ExecuteTransaction(
    IEnumerable<ITransactionHandler> handlers,
    IWalletRepository wallets,
    ITransactionRepository transactions,
    IIdempotencyRepository receipts,
    IUnitOfWork unitOfWork,
    TransactionRequestValidator validator)
{
    private readonly IReadOnlyDictionary<string, ITransactionHandler> handlersByType = handlers.ToDictionary(h => h.Type, StringComparer.Ordinal);

    public async Task<TransactionOutcome> ExecuteAsync(Actor actor, TransactionRequest request, string key, CancellationToken cancellationToken)
    {
        if (request.Type is null || !handlersByType.TryGetValue(request.Type, out var handler))
            throw new RequestFailure("invalid_transaction_type", FailureKind.Invalid);
        // Never expose a cached admin result before checking current permissions.
        Permissions.Require(actor, handler.Permission);
        if (!validator.Validate(request).IsValid) throw new RequestFailure("invalid_request", FailureKind.Invalid);
        if (string.IsNullOrEmpty(key) || key.Length > 128 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':')))
            throw new RequestFailure("invalid_idempotency_key", FailureKind.Invalid);
        var target = await handler.ResolveAsync(actor, request, cancellationToken);
        var money = Money.Parse(target.Currency, request.Amount!);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            type = handler.Type, target.PlayerId, target.Currency, target.BetId,
            amount = money.Amount.ToString("0.########", CultureInfo.InvariantCulture)
        }))));

        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            var existing = await receipts.ReserveAsync(target.PlayerId, key, fingerprint, cancellationToken);
            if (existing is not null)
            {
                if (existing.Fingerprint != fingerprint) throw new RequestFailure("idempotency_conflict", FailureKind.Conflict);
                await unitOfWork.CommitAsync(cancellationToken);
                return existing.Outcome;
            }
            var wallet = await wallets.LockAsync(target.PlayerId, target.Currency, cancellationToken)
                ?? throw new RequestFailure("wallet_not_found", FailureKind.NotFound);
            TransactionOutcome outcome;
            try
            {
                await handler.ApplyAsync(wallet, money, target, cancellationToken);
                // PostgreSQL stores microseconds; return and persist the same timestamp.
                var now = DateTimeOffset.UtcNow;
                var createdAt = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
                var entry = new LedgerEntry
                {
                    Id = Guid.NewGuid(), PlayerId = target.PlayerId, Currency = target.Currency, Kind = handler.Type,
                    Amount = money.Amount, BalanceAfter = wallet.Balance, BetId = target.BetId,
                    ActorIssuer = actor.Issuer, ActorSubject = actor.Subject, CreatedAt = createdAt
                };
                transactions.Add(entry);
                outcome = TransactionOutcome.Success(entry);
            }
            catch (WalletRuleException error)
            {
                // Business rejection is durable for this key, but never a ledger entry.
                outcome = TransactionOutcome.Rejected(error.Code);
            }
            await receipts.SaveAsync(target.PlayerId, key, outcome, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return outcome;
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
