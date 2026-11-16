using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MiniWallet.Wallet.Tests.Integration;

public sealed class WinPayoutApiTests(WalletDatabase database) : IClassFixture<WalletDatabase>
{
    private static async Task<JsonElement> Created(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> PlaceBet(HttpClient client, string subject, string amount = "10", string currency = "USD")
    {
        using var response = await TransactionTests.Post(client, new { type = "BET", currency, amount }, "bet", subject);
        return (await Created(response)).GetProperty("transactionId").GetGuid();
    }

    [Theory]
    [InlineData("USD", "10", null, null, "20")]
    [InlineData("USD", "10", "2", "5", "25")]
    [InlineData("EUR", "10", "1.5", "0.25", "15.25")]
    [InlineData("BTC", "0.00000002", "1.5", "0.00000001", "0.00000004")]
    public async Task Win_credits_calculated_payout_to_original_bet_owner_and_currency(
        string currency, string stake, string? multiplier, string? bonusAmount, string expected)
    {
        var owner = await database.SeedAsync();
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var funding = await TransactionTests.Post(client, new { type = "BONUS", playerId = owner.Id, currency, amount = "100" }, "fund", "admin", true);
        await Created(funding);
        var betId = await PlaceBet(client, owner.Subject, stake, currency);
        using var response = await TransactionTests.Post(client, new { type = "WIN", betId, multiplier, bonusAmount }, "win", "admin", true);
        var json = await Created(response);
        Assert.Equal(expected, json.GetProperty("amount").GetString());
        Assert.Equal(owner.Id, json.GetProperty("playerId").GetGuid());
        Assert.Equal(currency, json.GetProperty("currency").GetString());
        Assert.Equal(betId, json.GetProperty("betId").GetGuid());
        var balance = 100m - decimal.Parse(stake, CultureInfo.InvariantCulture) + decimal.Parse(expected, CultureInfo.InvariantCulture);
        Assert.Equal(balance, await database.BalanceAsync(owner.Id, currency));
        await using var db = database.Open();
        var stored = await db.Transactions.SingleAsync(t => t.PlayerId == owner.Id && t.Kind == "WIN");
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), stored.Amount);
        Assert.Equal(balance, stored.BalanceAfter);
    }

    [Fact]
    public async Task Omitted_defaults_and_normalized_values_replay_the_same_win()
    {
        var owner = await database.SeedAsync(100);
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        var betId = await PlaceBet(client, owner.Subject);
        using var first = await TransactionTests.Post(client, new { type = "WIN", betId }, "win", "admin", true);
        var json = await Created(first);
        using var replay = await TransactionTests.Post(client, new { type = "WIN", betId, multiplier = "02.00000000", bonusAmount = "00.00" }, "win", "admin", true);
        Assert.Equal(json.ToString(), (await Created(replay)).ToString());
        Assert.Equal("20", json.GetProperty("amount").GetString());
        Assert.Equal(110m, await database.BalanceAsync(owner.Id));
        Assert.Equal(1, await database.CountAsync(owner.Id, "WIN"));
        using var denied = await TransactionTests.Post(client, new { type = "WIN", betId }, "win", owner.Subject);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Theory]
    [InlineData("2.5", "0")]
    [InlineData("3", "5")]
    [InlineData("2", "6")]
    public async Task Changed_terms_conflict_even_when_total_is_the_same(string multiplier, string bonusAmount)
    {
        var owner = await database.SeedAsync(100);
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        var betId = await PlaceBet(client, owner.Subject);
        using var first = await TransactionTests.Post(client, new { type = "WIN", betId, multiplier = "2", bonusAmount = "5" }, "win", "admin", true);
        await Created(first);
        using var changed = await TransactionTests.Post(client, new { type = "WIN", betId, multiplier, bonusAmount }, "win", "admin", true);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("idempotency_conflict", (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(115m, await database.BalanceAsync(owner.Id));
        Assert.Equal(1, await database.CountAsync(owner.Id, "WIN"));
    }

    [Theory]
    [InlineData("multiplier", "0", "invalid_multiplier")]
    [InlineData("multiplier", "-2", "invalid_multiplier")]
    [InlineData("multiplier", "1e2", "invalid_multiplier")]
    [InlineData("multiplier", "", "invalid_multiplier")]
    [InlineData("multiplier", "1.0001", "invalid_payout_precision")]
    [InlineData("multiplier", "10000000000000000000", "invalid_payout_amount")]
    [InlineData("bonusAmount", "-1", "invalid_bonus_amount")]
    [InlineData("bonusAmount", "0.001", "invalid_bonus_amount")]
    [InlineData("bonusAmount", "100000000000000000000", "invalid_bonus_amount")]
    [InlineData("bonusAmount", "99999999999999999999.99", "invalid_payout_amount")]
    [InlineData("amount", "25", "invalid_request")]
    [InlineData("currency", "EUR", "invalid_request")]
    [InlineData("playerId", "00000000-0000-0000-0000-000000000001", "invalid_request")]
    public async Task Invalid_win_returns_400_without_mutation_or_reserving_key(string field, string value, string code)
    {
        var owner = await database.SeedAsync(100);
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        var betId = await PlaceBet(client, owner.Subject);
        var body = new Dictionary<string, object> { ["type"] = "WIN", ["betId"] = betId, [field] = value };
        using var invalid = await TransactionTests.Post(client, body, "win", "admin", true);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(code, (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(90m, await database.BalanceAsync(owner.Id));
        Assert.Equal(0, await database.CountAsync(owner.Id, "WIN"));
        await using (var db = database.Open())
            Assert.False(await db.Idempotency.AnyAsync(r => r.PlayerId == owner.Id && r.Key == "win"));
        using var corrected = await TransactionTests.Post(client, new { type = "WIN", betId }, "win", "admin", true);
        await Created(corrected);
        Assert.Equal(110m, await database.BalanceAsync(owner.Id));
    }

    [Theory]
    [InlineData("multiplier")]
    [InlineData("bonusAmount")]
    public async Task Win_requires_decimal_strings(string field)
    {
        var owner = await database.SeedAsync(100);
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        var betId = await PlaceBet(client, owner.Subject);
        using var response = await TransactionTests.Post(client,
            new Dictionary<string, object> { ["type"] = "WIN", ["betId"] = betId, [field] = 2 }, "win", "admin", true);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(90m, await database.BalanceAsync(owner.Id));
    }

    [Theory]
    [InlineData("BET", "multiplier")]
    [InlineData("BET", "bonusAmount")]
    [InlineData("BONUS", "multiplier")]
    [InlineData("BONUS", "bonusAmount")]
    public async Task Other_transaction_types_reject_win_fields(string type, string field)
    {
        var owner = await database.SeedAsync(100);
        using var app = new WalletApiFactory(database.ConnectionString); using var client = app.CreateClient();
        var body = new Dictionary<string, object> { ["type"] = type, ["currency"] = "USD", ["amount"] = "10", [field] = "2" };
        if (type == "BONUS") body["playerId"] = owner.Id;
        using var response = await TransactionTests.Post(client, body, "attempt", type == "BONUS" ? "admin" : owner.Subject, type == "BONUS");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(100m, await database.BalanceAsync(owner.Id));
    }
}
