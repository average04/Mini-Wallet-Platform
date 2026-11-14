using MiniWallet.Contracts;
using MiniWallet.Users.Domain;
namespace MiniWallet.Users.Application.Abstractions;

public interface IProfileRepository
{
    Task<PlayerProfile?> FindAsync(Guid playerId, CancellationToken cancellationToken);
    void Add(PlayerProfile profile);
}
public interface IPlayerEventOutbox
{
    void Add(PlayerCreatedV1 message);
}
public sealed class IdentityAlreadyExistsException(Exception inner) : Exception("The external identity already belongs to a player.", inner);

public static class Permissions
{
    public const string Create = "Profiles.Create";
    public const string ManageOwn = "Profiles.ManageOwn";
    public const string ManageAny = "Profiles.ManageAny";
    public static void Require(Actor actor, string permission)
    {
        if (!actor.Can(permission)) throw new RequestFailure("forbidden", FailureKind.Forbidden);
    }
}
