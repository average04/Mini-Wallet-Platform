using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
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
    public async Task Connecting_during_Redis_outage_fails_while_REST_still_works()
    {
        var player = await database.SeedAsync(100);
        var redisEndpoint = redis.ConnectionString;
        await redis.Container.StopAsync();
        try
        {
            using var factory = new WalletApiFactory(database.ConnectionString, redisEndpoint);
            using var client = factory.CreateClient();
            var failure = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await using var feed = await BalanceHubTests.Feed.ConnectAsync(factory, player.Subject);
            });
            Assert.IsNotType<TimeoutException>(failure);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", WalletApiFactory.Token(player.Subject));
            using var balances = await client.GetAsync("/api/v1/wallets/me");
            Assert.Equal(HttpStatusCode.OK, balances.StatusCode);
            using var response = await TransactionTests.Post(client, new { type = "BET", currency = "USD", amount = "10" }, "cold-redis-outage", player.Subject);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(90m, await database.BalanceAsync(player.Id));
        }
        finally { await redis.Container.StartAsync(); }
    }

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
        var redisEndpoint = redis.ConnectionString;
        using var factory = new WalletApiFactory(database.ConnectionString, redisEndpoint);
        using var client = factory.CreateClient();
        Assert.IsType<RedisHubLifetimeManager<BalanceHub>>(factory.Services.GetRequiredService<HubLifetimeManager<BalanceHub>>());
        await using var feed = await BalanceHubTests.Feed.ConnectAsync(factory, player.Subject);
        // Pause preserves the endpoint; Docker stop/start remaps dynamic test ports.
        await redis.Container.PauseAsync();
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
        finally { await redis.Container.UnpauseAsync(); }

        Assert.Equal(redisEndpoint, redis.ConnectionString);
        // A new connection waits for group enrollment, proving subscriptions work again.
        await using var recovered = await ConnectAfterRecoveryAsync(factory, player.Subject);
        using var afterRecovery = await TransactionTests.Post(client, new { type = "BET", currency = "USD", amount = "1" }, "redis-recovered", player.Subject);
        Assert.Equal(HttpStatusCode.Created, afterRecovery.StatusCode);
        Assert.Equal("89", (await recovered.NextAsync()).GetProperty("balanceAfter").GetString());
        var existingConnectionBalance = (await feed.NextAsync()).GetProperty("balanceAfter").GetString();
        // A publish already in flight when Redis pauses can finish after the notifier times out.
        if (existingConnectionBalance == "90")
            existingConnectionBalance = (await feed.NextAsync()).GetProperty("balanceAfter").GetString();
        Assert.Equal("89", existingConnectionBalance);
    }

    private static async Task<BalanceHubTests.Feed> ConnectAfterRecoveryAsync(WalletApiFactory factory, string subject)
    {
        // Container readiness precedes the existing Redis client's reconnect/backoff loop.
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try { return await BalanceHubTests.Feed.ConnectAsync(factory, subject); }
            catch (HttpRequestException) when (elapsed.Elapsed < TimeSpan.FromSeconds(15))
            {
                await Task.Delay(200);
            }
        }
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
