using System.Diagnostics;
using System.Net;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using MiniWallet.Wallet.Api.Realtime;

namespace MiniWallet.Wallet.Tests.Integration;

public sealed class BalanceBackplaneTests(WalletDatabase database, BalanceRedis redis)
    : IClassFixture<WalletDatabase>, IClassFixture<BalanceRedis>
{
    [Fact]
    public async Task Transaction_on_another_API_instance_reaches_the_connected_player()
    {
        var player = await database.SeedAsync(100);
        using var first = new WalletApiFactory(database.ConnectionString, redis.ConnectionString);
        using var second = new WalletApiFactory(database.ConnectionString, redis.ConnectionString);
        using var firstClient = first.CreateClient();
        using var secondClient = second.CreateClient();
        Assert.IsType<RedisHubLifetimeManager<BalanceHub>>(first.Services.GetRequiredService<HubLifetimeManager<BalanceHub>>());
        Assert.IsType<RedisHubLifetimeManager<BalanceHub>>(second.Services.GetRequiredService<HubLifetimeManager<BalanceHub>>());
        await using var feed = await BalanceHubTests.Feed.ConnectAsync(first, player.Subject);
        using var response = await TransactionTests.Post(secondClient, new { type = "BET", currency = "USD", amount = "10" }, "other-host", player.Subject);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("90", (await feed.NextAsync()).GetProperty("balanceAfter").GetString());
        Assert.Equal(90m, await database.BalanceAsync(player.Id));
    }

    [Fact]
    public async Task Redis_outage_does_not_fail_or_duplicate_a_committed_transaction()
    {
        var player = await database.SeedAsync(100);
        using var factory = new WalletApiFactory(database.ConnectionString, redis.ConnectionString);
        using var client = factory.CreateClient();
        Assert.IsType<RedisHubLifetimeManager<BalanceHub>>(factory.Services.GetRequiredService<HubLifetimeManager<BalanceHub>>());
        await using var feed = await BalanceHubTests.Feed.ConnectAsync(factory, player.Subject);
        await redis.Container.StopAsync();
        try
        {
            var elapsed = Stopwatch.StartNew();
            using var response = await TransactionTests.Post(client, new { type = "BET", currency = "USD", amount = "10" }, "redis-outage", player.Subject);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(6), "Notifications must not block the money response indefinitely");
            using var retry = await TransactionTests.Post(client, new { type = "BET", currency = "USD", amount = "10" }, "redis-outage", player.Subject);
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
            Assert.Equal(await response.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
            Assert.Equal(90m, await database.BalanceAsync(player.Id));
            Assert.Equal(1, await database.CountAsync(player.Id, "BET"));
        }
        finally { await redis.Container.StartAsync(); }
    }
}

public sealed class BalanceRedis : IAsyncLifetime
{
    public IContainer Container { get; } = new ContainerBuilder("redis:7.4.7-alpine")
        .WithPortBinding(6379, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping")).Build();
    public string ConnectionString => $"{Container.Hostname}:{Container.GetMappedPublicPort(6379)}";
    public Task InitializeAsync() => Container.StartAsync();
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}
