using System.Globalization;
using MediatR;
using MiniWallet.Wallet.Application.Abstractions;

namespace MiniWallet.Wallet.Application.Features.Reads;

public sealed record GetBalancesQuery(Actor Actor, Guid? PlayerId = null) : IRequest<IReadOnlyList<BalanceView>>;

public sealed class GetBalancesHandler(IIdentityRepository identities, IWalletReadRepository reads)
    : IRequestHandler<GetBalancesQuery, IReadOnlyList<BalanceView>>
{
    public async Task<IReadOnlyList<BalanceView>> Handle(GetBalancesQuery query, CancellationToken cancellationToken)
    {
        var target = await WalletAccess.ResolveAsync(query.Actor, query.PlayerId, identities, reads, cancellationToken);
        var balances = await reads.BalancesAsync(target, cancellationToken);
        return balances.Select(x => new BalanceView(x.Currency, x.Balance.ToString("0.########", CultureInfo.InvariantCulture))).ToArray();
    }
}
