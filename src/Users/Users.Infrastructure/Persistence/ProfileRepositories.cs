using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MiniWallet.Contracts;
using MiniWallet.Users.Application.Abstractions;
using MiniWallet.Users.Domain;
using Npgsql;
namespace MiniWallet.Users.Infrastructure.Persistence;

public sealed class ProfileRepository(UsersDbContext db) : IProfileRepository
{
    public Task<PlayerProfile?> FindAsync(Guid playerId, CancellationToken cancellationToken)
        => db.Profiles.SingleOrDefaultAsync(x => x.Id == playerId, cancellationToken);
    public void Add(PlayerProfile profile) => db.Profiles.Add(profile);
}
public sealed class IdentityRepository(UsersDbContext db) : IIdentityRepository
{
    public Task<Guid?> ResolvePlayerAsync(string issuer, string subject, CancellationToken cancellationToken)
        => db.Identities.Where(x => x.Issuer == issuer && x.Subject == subject).Select(x => (Guid?)x.PlayerId).SingleOrDefaultAsync(cancellationToken);
    public void Add(Guid playerId, string issuer, string subject)
        => db.Identities.Add(new IdentityAssociation { PlayerId = playerId, Issuer = issuer, Subject = subject });
}
public sealed class PlayerEventOutbox(UsersDbContext db) : IPlayerEventOutbox
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public void Add(PlayerCreatedV1 message) => db.Outbox.Add(new OutboxEvent
    {
        EventId = message.EventId, Type = "player.created.v1", OccurredAt = message.OccurredAt,
        PayloadJson = JsonSerializer.Serialize(message, Json)
    });
}
public sealed class UnitOfWork(UsersDbContext db) : IUnitOfWork
{
    public async Task BeginAsync(CancellationToken cancellationToken)
        => await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await db.Database.CommitTransactionAsync(cancellationToken);
            if (db.Database.CurrentTransaction is { } transaction) await transaction.DisposeAsync();
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_player_identities" })
        {
            throw new IdentityAlreadyExistsException(error);
        }
    }
    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is { } transaction)
        {
            try { await transaction.RollbackAsync(cancellationToken); }
            finally { await transaction.DisposeAsync(); }
        }
        db.ChangeTracker.Clear();
    }
}
