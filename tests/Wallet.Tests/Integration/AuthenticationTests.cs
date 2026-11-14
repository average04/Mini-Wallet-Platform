using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace MiniWallet.Wallet.Tests.Integration;

public sealed class AuthenticationTests
{
    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("unsigned")]
    [InlineData("subject")]
    public async Task Invalid_token_cannot_call_transactions(string reason)
    {
        using var factory = new WalletApiFactory(); using var client = factory.CreateClient();
        var token = reason switch
        {
            "issuer" => WalletApiFactory.Token(issuer: "https://untrusted.example"),
            "audience" => WalletApiFactory.Token(audience: "another-api"),
            "expired" => WalletApiFactory.Token(expired: true),
            "subject" => WalletApiFactory.Token(subject: ""),
            _ => ""
        };
        if (reason == "unsigned")
        {
            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(WalletApiFactory.Issuer, WalletApiFactory.Audience,
                [new System.Security.Claims.Claim("sub", "player")], DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5));
            token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", "unauthorized");
        request.Content = JsonContent.Create(new { type = "BET", currency = "USD", amount = "1" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Swagger_exposes_sign_in_and_required_idempotency_header()
    {
        using var factory = new WalletApiFactory(); using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("components").GetProperty("securitySchemes").GetProperty("oauth2")
            .GetProperty("flows").TryGetProperty("authorizationCode", out _));
        Assert.Contains(json.GetProperty("paths").GetProperty("/api/v1/transactions").GetProperty("post")
            .GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "Idempotency-Key"
            && p.GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task Browser_preflight_allows_only_configured_origin()
    {
        using var factory = new WalletApiFactory(); using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/transactions");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,idempotency-key,content-type");
        using var response = await client.SendAsync(request);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Equal("http://localhost:3000", Assert.Single(origins));
        using var denied = new HttpRequestMessage(HttpMethod.Options, "/api/v1/transactions");
        denied.Headers.Add("Origin", "https://untrusted.example");
        denied.Headers.Add("Access-Control-Request-Method", "POST");
        using var blocked = await client.SendAsync(denied);
        Assert.False(blocked.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
