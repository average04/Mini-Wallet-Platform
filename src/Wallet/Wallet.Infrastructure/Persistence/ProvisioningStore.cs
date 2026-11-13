using Microsoft.EntityFrameworkCore;
using MiniWallet.Wallet.Application.Features.Provisioning;
namespace MiniWallet.Wallet.Infrastructure.Persistence;

public sealed class ProvisioningStore(WalletDbContext db) : IProvisioningStore
{
    public async Task<bool> RecordEventAsync(Guid eventId, string payloadHash, CancellationToken cancellationToken)
    {
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO inbox (event_id, payload_hash, received_at) VALUES ({eventId}, {payloadHash}, CURRENT_TIMESTAMP) ON CONFLICT DO NOTHING", cancellationToken);
        if (inserted == 1) return true;
        var existing = await db.Inbox.AsNoTracking().SingleAsync(x => x.EventId == eventId, cancellationToken);
        if (existing.PayloadHash != payloadHash) throw new InvalidProvisioningMessage("Event ID reused with different content.");
        return false;
    }
    public async Task AssociateAsync(Guid playerId, string issuer, string subject, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO player_identities (issuer, subject, player_id) VALUES ({issuer}, {subject}, {playerId}) ON CONFLICT DO NOTHING", cancellationToken);
        var existing = await db.Identities.AsNoTracking().SingleAsync(x => x.Issuer == issuer && x.Subject == subject, cancellationToken);
        if (existing.PlayerId != playerId) throw new InvalidProvisioningMessage("Identity already belongs to another player.");
    }
    public async Task CreateWalletsAsync(Guid playerId, CancellationToken cancellationToken)
    {
        // Consistent insertion order avoids lock inversion across duplicate player events.
        foreach (var currency in new[] { "BTC", "EUR", "USD" })
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO wallets (player_id, currency, balance) VALUES ({playerId}, {currency}, 0) ON CONFLICT DO NOTHING", cancellationToken);
    }
}
