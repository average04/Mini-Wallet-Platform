using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MiniWallet.Contracts;
using MiniWallet.Wallet.Application.Features.Provisioning;
using MiniWallet.Wallet.Infrastructure.Messaging;
using MiniWallet.Wallet.Infrastructure.Persistence;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
namespace MiniWallet.Wallet.Tests.Integration;

public sealed class ConsumerRecoveryTests : IAsyncLifetime
{
    private readonly WalletDatabase database = new();
    private readonly RabbitMqContainer broker = new RabbitMqBuilder("rabbitmq:4.2.4-management").Build();
    public async Task InitializeAsync() => await Task.WhenAll(database.InitializeAsync(), broker.StartAsync());
    public async Task DisposeAsync() { await database.DisposeAsync(); await broker.DisposeAsync(); }
    [Fact]
    public async Task Consumer_retries_database_outage_then_redelivery_and_poison_are_safe()
    {
        await using var connection = await new ConnectionFactory { Uri = new Uri(broker.GetConnectionString()) }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        var consumer = new PlayerEventConsumer(broker.GetConnectionString(), NullLogger<PlayerEventConsumer>.Instance);
        var attempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task Handle(PlayerCreatedV1 message, CancellationToken ct) => Provision(message, ct);
        async Task Provision(PlayerCreatedV1 message, CancellationToken ct)
        {
            attempt.TrySetResult();
            await using var db = database.Open();
            await new ProvisionPlayer(new ProvisioningStore(db), new UnitOfWork(db)).ExecuteAsync(message, ct);
        }
        var running = consumer.RunAsync(Handle, stop.Token);
        try
        {
            await Eventually(async () =>
            {
                // Passive declarations use a throwaway channel: missing queue closes that channel.
                await using var probe = await connection.CreateChannelAsync();
                try { await probe.QueueDeclarePassiveAsync("mini-wallet.provisioning"); return true; }
                catch (RabbitMQ.Client.Exceptions.OperationInterruptedException) { return false; }
            });
            var message = new PlayerCreatedV1(Guid.NewGuid(), Guid.NewGuid(), "https://issuer.test", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
            await database.Container.StopAsync();
            await Publish(channel, message);
            await attempt.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(running.IsCompleted);
            await database.Container.StartAsync();
            await Eventually(async () => { await using var db = database.Open(); return await db.Inbox.AnyAsync(x => x.EventId == message.EventId); });
            await using (var db = database.Open())
            {
                Assert.Equal(3, await db.Wallets.CountAsync(x => x.PlayerId == message.PlayerId));
                (await db.Wallets.SingleAsync(x => x.PlayerId == message.PlayerId && x.Currency == "USD")).Credit(MiniWallet.Wallet.Domain.Money.Parse("USD", "50"));
                await db.SaveChangesAsync();
            }
            await Publish(channel, message);
            await Publish(channel, message with { EventId = Guid.NewGuid() });
            // FIFO + prefetch 1 makes the poison DLQ entry a completion barrier for preceding deliveries.
            await channel.BasicPublishAsync("mini-wallet.events", "player.created.v1", true,
                new BasicProperties { Persistent = true, Type = "player.created.v1", MessageId = Guid.NewGuid().ToString() }, Encoding.UTF8.GetBytes("{broken"), stop.Token);
            await Eventually(async () => (await channel.QueueDeclarePassiveAsync("mini-wallet.provisioning.dead")).MessageCount == 1);
            Assert.Equal(50, await database.BalanceAsync(message.PlayerId));
            await using var check = database.Open();
            Assert.Equal(3, await check.Wallets.CountAsync(x => x.PlayerId == message.PlayerId));
            Assert.Equal(2, await check.Inbox.CountAsync());
        }
        finally
        {
            await stop.CancelAsync();
            try { await running.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { }
        }
    }
    private static Task Publish(IChannel channel, PlayerCreatedV1 message) => channel.BasicPublishAsync(
        "mini-wallet.events", "player.created.v1", true,
        new BasicProperties { Persistent = true, Type = "player.created.v1", MessageId = message.EventId.ToString() },
        JsonSerializer.SerializeToUtf8Bytes(message, new JsonSerializerOptions(JsonSerializerDefaults.Web))).AsTask();
    private static async Task Eventually(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        while (!await condition()) await Task.Delay(100, timeout.Token);
    }
}
