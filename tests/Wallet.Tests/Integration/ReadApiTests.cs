using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace MiniWallet.Wallet.Tests.Integration;

public sealed class ReadApiTests(WalletDatabase database) : IClassFixture<WalletDatabase>
{
    private static async Task<HttpResponseMessage> Get(HttpClient client, string path, string subject, bool admin = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", WalletApiFactory.Token(subject, admin));
        return await client.SendAsync(request);
    }
    [Fact]
    public async Task Owner_and_admin_read_exact_balances_but_player_cannot_use_admin_route()
    {
        var player = await database.SeedAsync(12.34m);
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var own = await Get(client, "/api/v1/wallets/me", player.Subject);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        var wallets = await own.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, wallets.GetArrayLength());
        Assert.Equal("12.34", wallets.EnumerateArray().Single(x => x.GetProperty("currency").GetString() == "USD").GetProperty("balance").GetString());
        using var admin = await Get(client, $"/api/v1/players/{player.Id}/wallets", "admin-without-profile", true);
        Assert.Equal(await own.Content.ReadAsStringAsync(), await admin.Content.ReadAsStringAsync());
        using var forbidden = await Get(client, $"/api/v1/players/{player.Id}/wallets", player.Subject);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
    [Fact]
    public async Task Posted_location_resolves_only_for_owner_or_admin()
    {
        var player = await database.SeedAsync(100);
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var post = await TransactionTests.Post(client, new { type = "BET", currency = "USD", amount = "10" }, "location", player.Subject);
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        using var own = await Get(client, post.Headers.Location!.ToString(), player.Subject);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(await post.Content.ReadAsStringAsync(), await own.Content.ReadAsStringAsync());
        using var admin = await Get(client, post.Headers.Location.ToString(), "admin", true);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        using var foreign = await Get(client, post.Headers.Location.ToString(), "other");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var unknown = await Get(client, $"/api/v1/transactions/{Guid.NewGuid()}", player.Subject);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
    [Theory]
    [InlineData("/api/v1/wallets/me")]
    [InlineData("/api/v1/transactions/me")]
    public async Task Own_unprovisioned_reads_are_retryable(string path)
    {
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var response = await Get(client, path, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter!.Delta);
    }
    [Fact]
    public async Task Admin_unknown_player_is_404_and_player_history_admin_route_is_403()
    {
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        foreach (var suffix in new[] { "wallets", "transactions" })
        {
            using var response = await Get(client, $"/api/v1/players/{Guid.NewGuid()}/{suffix}", "admin", true);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using var forbidden = await Get(client, $"/api/v1/players/{Guid.NewGuid()}/{suffix}", "player");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
    }
    [Fact]
    public async Task Cursor_handles_tied_timestamps_and_ignores_newer_appends_without_duplicates()
    {
        var player = await database.SeedAsync(100);
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            using var post = await TransactionTests.Post(client, new { type = "BET", currency = "USD", amount = "1" }, $"tie-{i}", player.Subject);
            Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        }
        await using (var db = database.Open())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE transactions SET created_at = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE player_id = {player.Id}");
        using var first = await Get(client, "/api/v1/transactions/me?limit=2", player.Subject);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var page = await first.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("transactionId").GetGuid()).ToList();
        using var appended = await TransactionTests.Post(client, new { type = "BET", currency = "USD", amount = "1" }, "append", player.Subject);
        var appendedId = (await appended.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transactionId").GetGuid();
        while (page.GetProperty("nextCursor").GetString() is { } cursor)
        {
            using var next = await Get(client, "/api/v1/transactions/me?limit=2&cursor=" + Uri.EscapeDataString(cursor), player.Subject);
            Assert.Equal(HttpStatusCode.OK, next.StatusCode);
            page = await next.Content.ReadFromJsonAsync<JsonElement>();
            ids.AddRange(page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("transactionId").GetGuid()));
        }
        Assert.Equal(6, ids.Count);
        Assert.Equal(6, ids.Distinct().Count());
        Assert.DoesNotContain(appendedId, ids);
    }
    [Fact]
    public async Task Invalid_limit_malformed_and_cross_player_cursors_are_400()
    {
        var a = await database.SeedAsync(100); var b = await database.SeedAsync(100);
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var post = await TransactionTests.Post(client, new { type = "BET", currency = "USD", amount = "1" }, "cursor", a.Subject);
        using var first = await Get(client, "/api/v1/transactions/me?limit=1", a.Subject);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var cursor = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("nextCursor").GetString()!;
        foreach (var query in new[] { "limit=0", "limit=101", "limit=no", "cursor=not-a-cursor", "cursor=" + Uri.EscapeDataString(cursor) })
        {
            using var response = await Get(client, "/api/v1/transactions/me?" + query, b.Subject);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
    [Fact]
    public async Task Openapi_has_usable_transaction_examples_and_required_key()
    {
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        var doc = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var post = doc.GetProperty("paths").GetProperty("/api/v1/transactions").GetProperty("post");
        Assert.True(post.GetProperty("parameters").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Idempotency-Key").GetProperty("required").GetBoolean());
        var examples = post.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("examples");
        Assert.Equal("BET", examples.GetProperty("BET").GetProperty("value").GetProperty("type").GetString());
        Assert.Equal("WIN", examples.GetProperty("WIN").GetProperty("value").GetProperty("type").GetString());
        Assert.Equal("BONUS", examples.GetProperty("BONUS").GetProperty("value").GetProperty("type").GetString());
    }
}
