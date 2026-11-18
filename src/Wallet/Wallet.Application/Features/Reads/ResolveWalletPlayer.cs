using MediatR;
using MiniWallet.Wallet.Application.Abstractions;

namespace MiniWallet.Wallet.Application.Features.Reads;

public sealed record ResolveWalletPlayerQuery(Actor Actor) : IRequest<Guid>;

public sealed class ResolveWalletPlayerHandler(IIdentityRepository identities, IWalletReadRepository reads)
    : IRequestHandler<ResolveWalletPlayerQuery, Guid>
{
    public Task<Guid> Handle(ResolveWalletPlayerQuery query, CancellationToken cancellationToken)
        => WalletAccess.ResolveAsync(query.Actor, null, identities, reads, cancellationToken);
}
