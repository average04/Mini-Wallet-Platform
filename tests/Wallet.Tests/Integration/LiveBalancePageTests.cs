using System.Net;

namespace MiniWallet.Wallet.Tests.Integration;

public sealed class LiveBalancePageTests
{
    [Theory]
    [InlineData("/live-balances", "text/html")]
    [InlineData("/live-balances/app.mjs", "text/javascript")]
    [InlineData("/live-balances/vendor/signalr.min.js", "text/javascript")]
    public async Task Manual_client_assets_are_available_without_a_token(string path, string contentType)
    {
        using var factory = new WalletApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
    }
}
