using System.Globalization;
using MiniWallet.Wallet.Application.Abstractions;
using MiniWallet.Wallet.Domain;
namespace MiniWallet.Wallet.Application.Features.Transactions;

public interface ITransactionHandler
{
    string Type { get; }
    string Permission { get; }
    Task<TransactionTarget> ResolveAsync(Actor actor, TransactionRequest request, CancellationToken cancellationToken);
    Task ApplyAsync(BalanceAccount wallet, Money money, TransactionTarget target, CancellationToken cancellationToken);
}
public sealed class BetHandler(IIdentityRepository identities) : ITransactionHandler
{
    public string Type => "BET";
    public string Permission => Permissions.PlaceBet;
    public async Task<TransactionTarget> ResolveAsync(Actor actor, TransactionRequest request, CancellationToken cancellationToken)
    {
        var playerId = await identities.ResolvePlayerAsync(actor.Issuer, actor.Subject, cancellationToken)
            ?? throw new RequestFailure("player_not_provisioned", FailureKind.Conflict);
        return new TransactionTarget(playerId, Money.Parse(request.Currency!, request.Amount!));
    }
    public Task ApplyAsync(BalanceAccount wallet, Money money, TransactionTarget target, CancellationToken cancellationToken)
    {
        wallet.Debit(money);
        return Task.CompletedTask;
    }
}
public sealed class WinHandler(ITransactionRepository transactions) : ITransactionHandler
{
    public string Type => "WIN";
    public string Permission => Permissions.RecordWin;
    public async Task<TransactionTarget> ResolveAsync(Actor actor, TransactionRequest request, CancellationToken cancellationToken)
    {
        var bet = await transactions.FindAsync(request.BetId!.Value, cancellationToken);
        if (bet is null || bet.Kind != "BET") throw new RequestFailure("bet_not_found", FailureKind.NotFound);
        var stake = Money.Parse(bet.Currency, bet.Amount.ToString("0.########", CultureInfo.InvariantCulture));
        var payout = WinPayout.Calculate(stake, request.Multiplier, request.BonusAmount);
        return new TransactionTarget(bet.PlayerId, payout.Total, bet.Id, payout);
    }
    public async Task ApplyAsync(BalanceAccount wallet, Money money, TransactionTarget target, CancellationToken cancellationToken)
    {
        if (await transactions.IsSettledAsync(target.BetId!.Value, cancellationToken))
            throw new WalletRuleException("bet_already_settled");
        wallet.Credit(money);
    }
}
public sealed class BonusHandler : ITransactionHandler
{
    public string Type => "BONUS";
    public string Permission => Permissions.GrantBonus;
    public Task<TransactionTarget> ResolveAsync(Actor actor, TransactionRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new TransactionTarget(request.PlayerId!.Value, Money.Parse(request.Currency!, request.Amount!)));
    public Task ApplyAsync(BalanceAccount wallet, Money money, TransactionTarget target, CancellationToken cancellationToken)
    {
        wallet.Credit(money);
        return Task.CompletedTask;
    }
}
