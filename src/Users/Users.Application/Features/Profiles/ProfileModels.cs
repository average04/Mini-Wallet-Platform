using FluentValidation;
using MiniWallet.Users.Domain;
namespace MiniWallet.Users.Application.Features.Profiles;

public sealed record ProfileRequest(string? DisplayName);
public sealed record ProfileView(Guid PlayerId, string DisplayName, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static ProfileView From(PlayerProfile profile) => new(profile.Id, profile.DisplayName, profile.CreatedAt, profile.UpdatedAt);
}
public sealed record CreateProfileResult(ProfileView Profile, bool Created);
public sealed class ProfileRequestValidator : AbstractValidator<ProfileRequest>
{
    public ProfileRequestValidator()
        => RuleFor(x => x.DisplayName).NotEmpty().Must(name => name is not null && name.Trim().Length is >= 1 and <= 100);
}
