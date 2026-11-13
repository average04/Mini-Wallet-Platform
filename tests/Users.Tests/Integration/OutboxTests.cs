using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MiniWallet.Contracts;
using MiniWallet.Users.Application.Features.Outbox;
using MiniWallet.Users.Infrastructure.Messaging;
using MiniWallet.Users.Infrastructure.Persistence;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Testcontainers.RabbitMq;
namespace MiniWallet.Users.Tests.Integration;

public sealed class OutboxTests : IAsyncLifetime
{
    private readonly UsersDatabase database = new();
    private readonly RabbitMqContainer broker = new RabbitMqBuilder("rabbitmq:4.2.4-management").Build();
    public async Task InitializeAsync() => await Task.WhenAll(database.InitializeAsync(), broker.StartAsync());
    public async Task DisposeAsync() { await broker.DisposeAsync(); await database.DisposeAsync(); }
    private async Task<Guid> SeedAsync()
    {
        var message = new PlayerCreatedV1(Guid.NewGuid(), Guid.NewGuid(), "https://issuer.test", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        await using var db = database.Open();
        new PlayerEventOutbox(db).Add(message);
        await db.SaveChangesAsync();
        return message.EventId;
    }
    private async Task<bool> Dispatch(RabbitPublisher publisher)
    {
        await using var db = database.Open();
        return await new DispatchOutbox(new OutboxQueue(db), publisher, new UnitOfWork(db)).ExecuteAsync();
    }
    [Fact]
    public async Task Cold_publisher_declares_durable_route_without_consumer_and_two_publishers_claim_distinct_rows()
    {
        var first = await SeedAsync();
        var second = await SeedAsync();
        await using var publisher1 = new RabbitPublisher(broker.GetConnectionString());
        await using var publisher2 = new RabbitPublisher(broker.GetConnectionString());
        await Task.WhenAll(publisher1.InitializeAsync(), publisher2.InitializeAsync());
        Assert.All(await Task.WhenAll(Dispatch(publisher1), Dispatch(publisher2)), Assert.True);
        await using var connection = await new ConnectionFactory { Uri = new Uri(broker.GetConnectionString()) }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var received = new HashSet<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var message = await channel.BasicGetAsync("mini-wallet.provisioning", autoAck: true);
            Assert.NotNull(message);
            Assert.Equal(DeliveryModes.Persistent, message.BasicProperties.DeliveryMode);
            received.Add(JsonSerializer.Deserialize<PlayerCreatedV1>(message.Body.Span, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.EventId);
        }
        Assert.Contains(first, received);
        Assert.Contains(second, received);
        await using var db = database.Open();
        Assert.All(await db.Outbox.Where(x => x.EventId == first || x.EventId == second).ToListAsync(), x => Assert.NotNull(x.PublishedAt));
    }
    [Fact]
    public async Task Returned_then_confirmed_publish_remains_pending_and_recovers_on_reconnect()
    {
        var eventId = await SeedAsync();
        await using var publisher = new RabbitPublisher(broker.GetConnectionString());
        await publisher.InitializeAsync();
        await using var connection = await new ConnectionFactory { Uri = new Uri(broker.GetConnectionString()) }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueUnbindAsync("mini-wallet.provisioning", "mini-wallet.events", "player.created.v1");
        await Assert.ThrowsAsync<PublishReturnException>(() => Dispatch(publisher));
        await using (var db = database.Open()) Assert.Null((await db.Outbox.FindAsync(eventId))!.PublishedAt);
        await publisher.InitializeAsync(); // A failed publish reconnects and redeclares topology.
        Assert.True(await Dispatch(publisher));
        await using (var db = database.Open()) Assert.NotNull((await db.Outbox.FindAsync(eventId))!.PublishedAt);
        Assert.NotNull(await channel.BasicGetAsync("mini-wallet.provisioning", autoAck: true));
    }
}
