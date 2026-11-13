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
public sealed class GetProfile(IProfileRepository profiles, IIdentityRepository identities)
{
    public async Task<ProfileView> ExecuteAsync(Actor actor, Guid? playerId, CancellationToken cancellationToken)
    {
        var target = await ProfileAccess.ResolveAsync(actor, playerId, identities, cancellationToken);
        var profile = await profiles.FindAsync(target, cancellationToken)
            ?? throw new RequestFailure("profile_not_found", FailureKind.NotFound);
        return ProfileView.From(profile);
    }
}
public sealed class UpdateProfile(IProfileRepository profiles, IIdentityRepository identities,
    IUnitOfWork unitOfWork, ProfileRequestValidator validator)
{
    public async Task<ProfileView> ExecuteAsync(Actor actor, Guid? playerId, ProfileRequest request, CancellationToken cancellationToken)
    {
        var target = await ProfileAccess.ResolveAsync(actor, playerId, identities, cancellationToken);
        if (!validator.Validate(request).IsValid) throw new RequestFailure("invalid_display_name", FailureKind.Invalid);
        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            var profile = await profiles.FindAsync(target, cancellationToken)
                ?? throw new RequestFailure("profile_not_found", FailureKind.NotFound);
            profile.Rename(request.DisplayName!, DateTimeOffset.UtcNow);
            await unitOfWork.CommitAsync(cancellationToken);
            return ProfileView.From(profile);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
