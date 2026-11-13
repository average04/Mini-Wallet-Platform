namespace MiniWallet.Users.Application.Abstractions;

public sealed record Actor(string Issuer, string Subject, bool EmailVerified, IReadOnlySet<string> Permissions)
{
    public bool Can(string permission) => Permissions.Contains(permission);
}

public interface IIdentityRepository
{
    Task<Guid?> ResolvePlayerAsync(string issuer, string subject, CancellationToken cancellationToken);
    void Add(Guid playerId, string issuer, string subject);
}

public interface IUnitOfWork
{
    Task BeginAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}

