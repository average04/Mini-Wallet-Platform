using MediatR;
using MiniWallet.Users.Application.Abstractions;

namespace MiniWallet.Users.Application.Features.Profiles;

public sealed record GetProfileQuery(Actor Actor, Guid? PlayerId = null) : IRequest<ProfileView>;

public sealed class GetProfileHandler(IProfileRepository profiles, IIdentityRepository identities)
    : IRequestHandler<GetProfileQuery, ProfileView>
{
    public async Task<ProfileView> Handle(GetProfileQuery query, CancellationToken cancellationToken)
    {
        var target = await ProfileAccess.ResolveAsync(query.Actor, query.PlayerId, identities, cancellationToken);
        var profile = await profiles.FindAsync(target, cancellationToken)
            ?? throw new RequestFailure("profile_not_found", FailureKind.NotFound);
        return ProfileView.From(profile);
    }
}
