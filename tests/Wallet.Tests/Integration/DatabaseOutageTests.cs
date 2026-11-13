using System.Net;
using Microsoft.EntityFrameworkCore;
namespace MiniWallet.Wallet.Tests.Integration;

public sealed class DatabaseOutageTests : IAsyncLifetime
{
    private readonly WalletDatabase database = new();
    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();
    [Fact]
    public async Task Unavailable_database_never_returns_success_and_same_key_recovers_once()
    {
        var player = await database.SeedAsync(100);
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(database.ConnectionString) { Timeout = 2 }.ConnectionString;
        using var app = new WalletApiFactory(connection); using var client = app.CreateClient();
        var body = new { type = "BET", currency = "USD", amount = "10" };
        // Pause preserves the endpoint. Docker stop/start remaps dynamic test ports.
        await database.Container.PauseAsync();
        try
        {
            using var unavailable = await TransactionTests.Post(client, body, "outage-retry", player.Subject);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(2), unavailable.Headers.RetryAfter!.Delta);
        }
        finally { await database.Container.UnpauseAsync(); }
        Assert.Equal(new Npgsql.NpgsqlConnectionStringBuilder(connection).Port, database.Container.GetMappedPublicPort(5432));
        using var recovered = await TransactionTests.Post(client, body, "outage-retry", player.Subject);
        using var replayed = await TransactionTests.Post(client, body, "outage-retry", player.Subject);
        Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        Assert.Equal(await recovered.Content.ReadAsStringAsync(), await replayed.Content.ReadAsStringAsync());
        Assert.Equal(90, await database.BalanceAsync(player.Id));
        Assert.Equal(1, await database.CountAsync(player.Id, "BET"));
        await using var db = database.Open();
        Assert.Equal(1, await db.Idempotency.CountAsync(x => x.PlayerId == player.Id));
    }
}
