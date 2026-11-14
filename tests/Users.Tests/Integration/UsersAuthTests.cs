using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
namespace MiniWallet.Users.Tests.Integration;

public sealed class UsersAuthTests
{
    [Fact]
    public async Task Business_paths_require_bearer_authentication()
    {
        using var factory = new WebApplicationFactory<Api.Program>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/profiles/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_is_public_and_advertises_authorization_code_flow()
    {
        using var factory = new WebApplicationFactory<Api.Program>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty("oauth2").GetProperty("flows").TryGetProperty("authorizationCode", out _));
    }
}
