using MiniWallet.Contracts;
using MiniWallet.Users.Application.Abstractions;
using MiniWallet.Users.Domain;
namespace MiniWallet.Users.Application.Features.Profiles;

public sealed class CreateProfile(IProfileRepository profiles, IIdentityRepository identities, IPlayerEventOutbox outbox,
    IUnitOfWork unitOfWork, ProfileRequestValidator validator)
{
    public async Task<CreateProfileResult> ExecuteAsync(Actor actor, ProfileRequest request, CancellationToken cancellationToken)
    {
        Permissions.Require(actor, Permissions.Create);
        if (!actor.EmailVerified) throw new RequestFailure("email_not_verified", FailureKind.Forbidden);
        if (!validator.Validate(request).IsValid) throw new RequestFailure("invalid_display_name", FailureKind.Invalid);
        var existing = await identities.ResolvePlayerAsync(actor.Issuer, actor.Subject, cancellationToken);
        if (existing is not null) return new(await Existing(existing.Value, cancellationToken), false);

        var now = DateTimeOffset.UtcNow;
        var profile = new PlayerProfile(Guid.NewGuid(), request.DisplayName!, now);
        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            profiles.Add(profile);
            identities.Add(profile.Id, actor.Issuer, actor.Subject);
            outbox.Add(new PlayerCreatedV1(Guid.NewGuid(), profile.Id, actor.Issuer, actor.Subject, now));
            await unitOfWork.CommitAsync(cancellationToken);
            return new(ProfileView.From(profile), true);
        }
        catch (IdentityAlreadyExistsException)
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            // The uniqueness winner has committed. Return it without creating another event.
            var winner = await identities.ResolvePlayerAsync(actor.Issuer, actor.Subject, cancellationToken)
                ?? throw new InvalidOperationException("Identity uniqueness winner could not be resolved.");
            return new(await Existing(winner, cancellationToken), false);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
    private async Task<ProfileView> Existing(Guid playerId, CancellationToken cancellationToken)
        => ProfileView.From(await profiles.FindAsync(playerId, cancellationToken)
            ?? throw new InvalidOperationException("Identity association points to a missing profile."));
}
