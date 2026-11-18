using MediatR;
using MiniWallet.Users.Application.Abstractions;

namespace MiniWallet.Users.Application.Features.Profiles;

public sealed record UpdateProfileCommand(Actor Actor, ProfileRequest Profile, Guid? PlayerId = null) : IRequest<ProfileView>;

public sealed class UpdateProfileHandler(IProfileRepository profiles, IIdentityRepository identities,
    IUnitOfWork unitOfWork, ProfileRequestValidator validator) : IRequestHandler<UpdateProfileCommand, ProfileView>
{
    public async Task<ProfileView> Handle(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        var target = await ProfileAccess.ResolveAsync(command.Actor, command.PlayerId, identities, cancellationToken);
        if (!validator.Validate(command.Profile).IsValid) throw new RequestFailure("invalid_display_name", FailureKind.Invalid);
        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            var profile = await profiles.FindAsync(target, cancellationToken)
                ?? throw new RequestFailure("profile_not_found", FailureKind.NotFound);
            profile.Rename(command.Profile.DisplayName!, DateTimeOffset.UtcNow);
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
