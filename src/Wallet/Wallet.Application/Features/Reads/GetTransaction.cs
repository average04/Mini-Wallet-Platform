using MediatR;
using MiniWallet.Wallet.Application.Abstractions;
using MiniWallet.Wallet.Application.Features.Transactions;

namespace MiniWallet.Wallet.Application.Features.Reads;

public sealed record GetTransactionQuery(Actor Actor, Guid TransactionId) : IRequest<TransactionView>;

public sealed class GetTransactionHandler(IIdentityRepository identities, ITransactionRepository transactions)
    : IRequestHandler<GetTransactionQuery, TransactionView>
{
    public async Task<TransactionView> Handle(GetTransactionQuery query, CancellationToken cancellationToken)
    {
        var actor = query.Actor;
        var entry = await transactions.FindAsync(query.TransactionId, cancellationToken);
        if (entry is null || (!actor.Can(Permissions.ReadAny)
            && (!actor.Can(Permissions.PlaceBet) || await identities.ResolvePlayerAsync(actor.Issuer, actor.Subject, cancellationToken) != entry.PlayerId)))
            throw new RequestFailure("transaction_not_found", FailureKind.NotFound);
        return TransactionView.From(entry);
    }
}
