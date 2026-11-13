using Microsoft.EntityFrameworkCore;
using MiniWallet.Contracts;
using MiniWallet.Wallet.Application.Features.Provisioning;
using MiniWallet.Wallet.Infrastructure.Persistence;
using MiniWallet.Wallet.Domain;
namespace MiniWallet.Wallet.Tests.Integration;

public sealed class ProvisioningTests(WalletDatabase database) : IClassFixture<WalletDatabase>
{
    private static PlayerCreatedV1 Message() => new(Guid.NewGuid(), Guid.NewGuid(), "https://issuer.test", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
    private async Task Provision(PlayerCreatedV1 message)
    {
        await using var db = database.Open();
        await new ProvisionPlayer(new ProvisioningStore(db), new UnitOfWork(db)).ExecuteAsync(message);
    }
    [Fact]
    public async Task Delivery_creates_identity_inbox_and_three_zero_wallets_atomically()
    {
        var message = Message();
        await Provision(message);
        await using var db = database.Open();
        Assert.Equal(new[] { "BTC", "EUR", "USD" }, await db.Wallets.Where(x => x.PlayerId == message.PlayerId).OrderBy(x => x.Currency).Select(x => x.Currency).ToArrayAsync());
        Assert.All(await db.Wallets.Where(x => x.PlayerId == message.PlayerId).ToArrayAsync(), x => Assert.Equal(0, x.Balance));
        Assert.Equal(message.PlayerId, (await db.Identities.SingleAsync(x => x.Subject == message.Subject)).PlayerId);
        Assert.True(await db.Inbox.AnyAsync(x => x.EventId == message.EventId));
    }
    [Fact]
    public async Task Concurrent_redelivery_and_new_event_never_reset_funded_wallet()
    {
        var message = Message();
        await Task.WhenAll(Provision(message), Provision(message));
        await using (var db = database.Open())
        {
            (await db.Wallets.SingleAsync(x => x.PlayerId == message.PlayerId && x.Currency == "USD")).Credit(Money.Parse("USD", "27"));
            await db.SaveChangesAsync();
        }
        await Provision(message);
        await Provision(message with { EventId = Guid.NewGuid() });
        Assert.Equal(27, await database.BalanceAsync(message.PlayerId));
        await using var check = database.Open();
        Assert.Equal(3, await check.Wallets.CountAsync(x => x.PlayerId == message.PlayerId));
    }
    [Fact]
    public async Task Same_event_with_changed_payload_is_rejected_without_new_wallets()
    {
        var message = Message();
        await Provision(message);
        var changed = message with { PlayerId = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidProvisioningMessage>(() => Provision(changed));
        await using var db = database.Open();
        Assert.False(await db.Wallets.AnyAsync(x => x.PlayerId == changed.PlayerId));
    }
    [Fact]
    public async Task Conflicting_identity_rolls_back_inbox_and_wallets()
    {
        var message = Message();
        await Provision(message);
        var changed = message with { EventId = Guid.NewGuid(), PlayerId = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidProvisioningMessage>(() => Provision(changed));
        await using var db = database.Open();
        Assert.False(await db.Inbox.AnyAsync(x => x.EventId == changed.EventId));
        Assert.False(await db.Wallets.AnyAsync(x => x.PlayerId == changed.PlayerId));
    }
    [Fact]
    public async Task Malformed_event_is_rejected_before_writing()
    {
        var message = Message() with { Subject = "" };
        await Assert.ThrowsAsync<InvalidProvisioningMessage>(() => Provision(message));
        await using var db = database.Open();
        Assert.False(await db.Inbox.AnyAsync(x => x.EventId == message.EventId));
    }
}
