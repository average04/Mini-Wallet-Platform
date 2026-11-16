using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
namespace MiniWallet.Wallet.Tests.Integration;

public sealed class AuthContractTests : IDisposable
{
    private readonly WalletApiFactory factory = new();
    private readonly HttpClient client;
    public AuthContractTests() => client = factory.CreateClient();
    public void Dispose() { client.Dispose(); factory.Dispose(); }

    [Fact]
    public async Task Transaction_requires_a_valid_access_token()
    {
        using var response = await client.PostAsJsonAsync("/api/v1/transactions", new { type = "BET", currency = "USD", amount = "1" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("WIN")]
    [InlineData("BONUS")]
    public async Task Player_cannot_credit_any_wallet(string type)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", WalletApiFactory.Token());
        request.Headers.Add("Idempotency-Key", "denied");
        request.Content = JsonContent.Create(type == "WIN"
            ? (object)new { type, betId = Guid.NewGuid(), multiplier = "2" }
            : new { type, playerId = Guid.NewGuid(), currency = "USD", amount = "1" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
