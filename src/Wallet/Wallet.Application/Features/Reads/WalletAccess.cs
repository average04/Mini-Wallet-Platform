using MiniWallet.Wallet.Application.Abstractions;

namespace MiniWallet.Wallet.Application.Features.Reads;

internal static class WalletAccess
{
    public static async Task<Guid> ResolveAsync(Actor actor, Guid? playerId,
        IIdentityRepository identities, IWalletReadRepository reads, CancellationToken ct)
    {
        if (playerId is { } target)
        {
            Permissions.Require(actor, Permissions.ReadAny);
            if (!await reads.ExistsAsync(target, ct)) throw new RequestFailure("player_not_found", FailureKind.NotFound);
            return target;
        }
        if (!actor.Can(Permissions.PlaceBet) && !actor.Can(Permissions.ReadAny))
            throw new RequestFailure("forbidden", FailureKind.Forbidden);
        return await identities.ResolvePlayerAsync(actor.Issuer, actor.Subject, ct)
            ?? throw new RequestFailure("player_not_provisioned", FailureKind.Conflict);
    }
}
