using MiniWallet.Users.Application.Abstractions;
namespace MiniWallet.Users.Application.Features.Profiles;

internal static class ProfileAccess
{
    public static async Task<Guid> ResolveAsync(Actor actor, Guid? requestedPlayerId, IIdentityRepository identities, CancellationToken cancellationToken)
    {
        if (requestedPlayerId.HasValue)
        {
            Permissions.Require(actor, Permissions.ManageAny);
            return requestedPlayerId.Value;
        }
        Permissions.Require(actor, Permissions.ManageOwn);
        return await identities.ResolvePlayerAsync(actor.Issuer, actor.Subject, cancellationToken)
            ?? throw new RequestFailure("profile_not_found", FailureKind.NotFound);
    }
}
