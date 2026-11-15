using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MiniWallet.Wallet.Tests.Integration;

public sealed class TransactionTests(WalletDatabase database) : IClassFixture<WalletDatabase>
{
    private static object Bet(string amount) => new { type = "BET", currency = "USD", amount };
    private static object Bonus(Guid id, string amount) => new { type = "BONUS", playerId = id, currency = "USD", amount };
    private static object Win(Guid id) => new { type = "WIN", betId = id, amount = "15" };

    internal static async Task<HttpResponseMessage> Post(HttpClient client, object body, string key, string subject, bool admin = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", WalletApiFactory.Token(subject, admin));
        request.Headers.Add("Idempotency-Key", key);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    private static async Task<Guid> Id(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("transactionId").GetGuid();
    }

    [Fact]
    public async Task Two_hosts_cannot_spend_the_same_funds()
    {
        var player = await database.SeedAsync(100);
        using var a = new WalletApiFactory(database.ConnectionString);
        using var b = new WalletApiFactory(database.ConnectionString);
        using var clientA = a.CreateClient(); using var clientB = b.CreateClient();
        var replies = await Task.WhenAll(Post(clientA, Bet("80"), "bet-a", player.Subject), Post(clientB, Bet("80"), "bet-b", player.Subject));
        Assert.Equal(1, replies.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, replies.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(20m, await database.BalanceAsync(player.Id));
        Assert.Equal(1, await database.CountAsync(player.Id, "BET"));
        foreach (var reply in replies) reply.Dispose();
    }

    [Fact]
    public async Task Concurrent_same_key_has_one_effect_across_hosts()
    {
        var player = await database.SeedAsync(100);
        using var a = new WalletApiFactory(database.ConnectionString); using var b = new WalletApiFactory(database.ConnectionString);
        using var clientA = a.CreateClient(); using var clientB = b.CreateClient();
        using var first = await Post(clientA, Bet("10.00"), "same", player.Subject);
        var replies = await Task.WhenAll(Post(clientA, Bet("10.0"), "same", player.Subject), Post(clientB, Bet("10"), "same", player.Subject));
        var id = await Id(first);
        foreach (var reply in replies) { Assert.Equal(id, await Id(reply)); Assert.Equal(first.Headers.Location, reply.Headers.Location); reply.Dispose(); }
        Assert.Equal(90m, await database.BalanceAsync(player.Id));
        Assert.Equal(1, await database.CountAsync(player.Id, "BET"));
    }

    [Fact]
    public async Task First_in_flight_same_key_is_reserved_atomically()
    {
        var player = await database.SeedAsync(100);
        using var a = new WalletApiFactory(database.ConnectionString); using var b = new WalletApiFactory(database.ConnectionString);
        using var ca = a.CreateClient(); using var cb = b.CreateClient();
        var replies = await Task.WhenAll(Post(ca, Bet("10"), "new", player.Subject), Post(cb, Bet("10"), "new", player.Subject));
        Assert.Equal(await Id(replies[0]), await Id(replies[1]));
        Assert.Equal(90m, await database.BalanceAsync(player.Id));
        foreach (var reply in replies) reply.Dispose();
    }

    [Fact]
    public async Task Same_key_different_payload_conflicts()
    {
        var player = await database.SeedAsync(100);
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var first = await Post(client, Bet("10"), "same", player.Subject); await Id(first);
        using var changed = await Post(client, Bet("20"), "same", player.Subject);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("idempotency_conflict", (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(90m, await database.BalanceAsync(player.Id));
    }

    [Fact]
    public async Task Admin_key_scope_is_per_target_player_and_bonus_replays_once()
    {
        var a = await database.SeedAsync(); var b = await database.SeedAsync();
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var first = await Post(client, Bonus(a.Id, "100"), "grant", "admin", true);
        using var second = await Post(client, Bonus(b.Id, "100"), "grant", "admin", true);
        using var replay = await Post(client, Bonus(a.Id, "100"), "grant", "admin", true);
        var firstId = await Id(first);
        Assert.Equal(firstId, await Id(replay));
        Assert.NotEqual(firstId, await Id(second));
        Assert.Equal(100m, await database.BalanceAsync(a.Id));
        Assert.Equal(100m, await database.BalanceAsync(b.Id));
    }

    [Fact]
    public async Task Retry_after_ignoring_response_returns_committed_operation()
    {
        var player = await database.SeedAsync(100);
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        (await Post(client, Bet("10"), "lost-response", player.Subject)).Dispose();
        using var replay = await Post(client, Bet("10"), "lost-response", player.Subject);
        var id = await Id(replay);
        await using var db = database.Open();
        Assert.Equal(id, (await db.Transactions.SingleAsync(t => t.PlayerId == player.Id && t.Kind == "BET")).Id);
        Assert.Equal(90m, await database.BalanceAsync(player.Id));
    }

    [Fact]
    public async Task Cached_insufficient_funds_stays_failed_after_funding_and_has_no_history()
    {
        var player = await database.SeedAsync();
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var rejected = await Post(client, Bet("10"), "attempt", player.Subject);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        using var funded = await Post(client, Bonus(player.Id, "100"), "grant", "admin", true); await Id(funded);
        using var replay = await Post(client, Bet("10"), "attempt", player.Subject);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(0, await database.CountAsync(player.Id, "BET"));
        Assert.Equal(100m, await database.BalanceAsync(player.Id));
        using var newAttempt = await Post(client, Bet("10"), "new-attempt", player.Subject); await Id(newAttempt);
        Assert.Equal(90m, await database.BalanceAsync(player.Id));
    }

    [Fact]
    public async Task Different_keys_cannot_settle_a_bet_twice()
    {
        var player = await database.SeedAsync(100);
        using var a = new WalletApiFactory(database.ConnectionString); using var b = new WalletApiFactory(database.ConnectionString);
        using var ca = a.CreateClient(); using var cb = b.CreateClient();
        using var bet = await Post(ca, Bet("10"), "bet", player.Subject);
        var betId = await Id(bet);
        var replies = await Task.WhenAll(Post(ca, Win(betId), "win-a", "admin", true), Post(cb, Win(betId), "win-b", "admin", true));
        Assert.Equal(1, replies.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, replies.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(105m, await database.BalanceAsync(player.Id));
        Assert.Equal(1, await database.CountAsync(player.Id, "WIN"));
        foreach (var reply in replies) reply.Dispose();
    }

    [Fact]
    public async Task Win_must_reference_a_successful_bet()
    {
        var player = await database.SeedAsync();
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var bonus = await Post(client, Bonus(player.Id, "100"), "grant", "admin", true);
        using var invalid = await Post(client, Win(await Id(bonus)), "win", "admin", true);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        Assert.Equal(100m, await database.BalanceAsync(player.Id));
    }

    [Fact]
    public async Task Fresh_authorization_is_required_even_for_a_successful_replay()
    {
        var player = await database.SeedAsync();
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var grant = await Post(client, Bonus(player.Id, "100"), "grant", "admin", true); await Id(grant);
        using var stolenReplay = await Post(client, Bonus(player.Id, "100"), "grant", player.Subject);
        Assert.Equal(HttpStatusCode.Forbidden, stolenReplay.StatusCode);
        Assert.Equal(100m, await database.BalanceAsync(player.Id));
    }

    [Fact]
    public async Task Caller_cannot_select_someone_elses_wallet_for_a_bet()
    {
        var player = await database.SeedAsync(100); var other = await database.SeedAsync(100);
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var response = await Post(client, new { type = "BET", playerId = other.Id, currency = "USD", amount = "10" }, "attack", player.Subject);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(100m, await database.BalanceAsync(other.Id));
        Assert.Equal(100m, await database.BalanceAsync(player.Id));
    }

    [Theory]
    [InlineData("1.001")]
    [InlineData("0")]
    [InlineData("100000000000000000000")]
    public async Task Invalid_amount_does_not_reserve_the_key(string amount)
    {
        var player = await database.SeedAsync(100);
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var invalid = await Post(client, Bet(amount), "reuse-after-invalid", player.Subject);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var valid = await Post(client, Bet("1"), "reuse-after-invalid", player.Subject); await Id(valid);
        Assert.Equal(99m, await database.BalanceAsync(player.Id));
    }

    [Fact]
    public async Task Unprovisioned_player_has_retryable_conflict()
    {
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var response = await Post(client, Bet("1"), "pending", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task Database_also_rejects_a_negative_balance()
    {
        var player = await database.SeedAsync();
        await using var db = database.Open();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE wallets SET balance = -1 WHERE player_id = {player.Id} AND currency = 'USD'"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task Commit_failure_rolls_back_money_history_and_key_then_allows_retry()
    {
        var player = await database.SeedAsync(100);
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        await using var feed = await BalanceHubTests.Feed.ConnectAsync(app, player.Subject);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        // Test-owned UUID literals and identifier only; force the final ledger insert to fail.
        var constraint = "test_failure_" + player.Id.ToString("N");
        await using var add = new NpgsqlCommand($"ALTER TABLE transactions ADD CONSTRAINT {constraint} CHECK (player_id <> '{player.Id}'::uuid OR kind <> 'BET')", connection);
        await add.ExecuteNonQueryAsync();
        try
        {
            using var failed = await Post(client, Bet("10"), "retry", player.Subject);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Equal(100m, await database.BalanceAsync(player.Id));
            Assert.Equal(0, await database.CountAsync(player.Id, "BET"));
            await using var db = database.Open();
            Assert.False(await db.Idempotency.AnyAsync(x => x.PlayerId == player.Id));
            await feed.AssertQuietAsync();
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"ALTER TABLE transactions DROP CONSTRAINT {constraint}", connection);
            await drop.ExecuteNonQueryAsync();
        }
        using var retry = await Post(client, Bet("10"), "retry", player.Subject); await Id(retry);
        Assert.Equal(90m, await database.BalanceAsync(player.Id));
        Assert.Equal("90", (await feed.NextAsync()).GetProperty("balanceAfter").GetString());
    }
}
