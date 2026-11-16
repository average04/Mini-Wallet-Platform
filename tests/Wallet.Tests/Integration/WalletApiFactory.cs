using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MiniWallet.Wallet.Api.Realtime;

namespace MiniWallet.Wallet.Tests.Integration;

public sealed class WalletApiFactory(string connectionString = "Host=localhost;Database=unused;Username=unused", string? redisConnectionString = null) : WebApplicationFactory<Api.Program>
{
    public const string Issuer = "https://test-authority.example";
    public const string Audience = "mini-wallet-api";
    private static readonly RSA Rsa = RSA.Create(2048);
    private static readonly RsaSecurityKey Key = new(Rsa) { KeyId = "wallet-test-authority" };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = connectionString,
            ["ConnectionStrings:Redis"] = redisConnectionString,
            ["Auth:Issuer"] = Issuer,
            ["Auth:Audience"] = Audience,
            ["Auth:MetadataAddress"] = Issuer + "/.well-known/openid-configuration"
        }));
        // WebApplicationFactory applies its configuration after the minimal host's initial
        // service-registration code. Apply the same composition using the test container endpoint.
        builder.ConfigureServices((context, services) => services.AddBalanceUpdates(context.Configuration));
        builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.ConfigurationManager = null;
            options.Configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration { Issuer = Issuer };
            options.TokenValidationParameters.IssuerSigningKey = Key;
            options.TokenValidationParameters.ValidIssuer = Issuer;
            options.TokenValidationParameters.ValidAudience = Audience;
        }));
    }

    public static string Token(string subject = "player", bool admin = false, string? issuer = null, string? audience = null,
        bool expired = false, DateTime? expiresAt = null)
    {
        var claims = new List<Claim> { new("sub", subject), new("email_verified", "true", ClaimValueTypes.Boolean),
            new("realm_access", admin ? "{\"roles\":[\"admin\",\"player\"]}" : "{\"roles\":[\"player\"]}", JsonClaimValueTypes.Json) };
        var token = new JwtSecurityToken(issuer ?? Issuer, audience ?? Audience, claims,
            DateTime.UtcNow.AddHours(-2), expiresAt ?? (expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddMinutes(15)),
            new SigningCredentials(Key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
