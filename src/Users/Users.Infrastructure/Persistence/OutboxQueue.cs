using Microsoft.EntityFrameworkCore;
using MiniWallet.Users.Application.Features.Outbox;
namespace MiniWallet.Users.Infrastructure.Persistence;
public sealed class OutboxQueue(UsersDbContext db) : IOutboxQueue
{
    public async Task<OutgoingEvent?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Outbox.FromSqlRaw(
            "SELECT * FROM outbox WHERE published_at IS NULL ORDER BY occurred_at, event_id LIMIT 1 FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
        var row = rows.SingleOrDefault();
        return row is null ? null : new OutgoingEvent(row.EventId, row.Type, row.PayloadJson);
    }
    public Task MarkPublishedAsync(Guid eventId, CancellationToken cancellationToken)
        => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE outbox SET published_at = CURRENT_TIMESTAMP WHERE event_id = {eventId}", cancellationToken);
}
