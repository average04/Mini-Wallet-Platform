namespace MiniWallet.Wallet.Application.Abstractions;

public sealed record Actor(string Issuer, string Subject, bool EmailVerified, IReadOnlySet<string> Permissions)
{
    public bool Can(string permission) => Permissions.Contains(permission);
}

public interface IIdentityRepository
{
    Task<Guid?> ResolvePlayerAsync(string issuer, string subject, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task BeginAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}
