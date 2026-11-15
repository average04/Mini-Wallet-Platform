using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MiniWallet.Wallet.Api.Realtime;

namespace MiniWallet.Wallet.Api.Auth;

public static class AuthenticationSetup
{
    public static IServiceCollection AddPlatformAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.Authority = configuration["Auth:Issuer"] ?? "http://localhost:8080/realms/mini-wallet";
            options.MetadataAddress = configuration["Auth:MetadataAddress"] ?? options.Authority + "/.well-known/openid-configuration";
            options.RequireHttpsMetadata = !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = options.Authority,
                ValidateAudience = true,
                ValidAudience = configuration["Auth:Audience"] ?? "mini-wallet-api",
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(15)
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // Browser WebSockets/SSE cannot set an Authorization header. Never accept query
                    // tokens for REST, negotiation, or a similarly prefixed route.
                    if (context.Request.Path == BalanceHub.Path
                        && (context.HttpContext.WebSockets.IsWebSocketRequest
                            || context.Request.Headers.Accept.Any(value => value?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true)))
                        context.Token = context.Request.Query["access_token"];
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    if (string.IsNullOrWhiteSpace(context.Principal?.FindFirst("sub")?.Value))
                        context.Fail("A subject is required.");
                    return Task.CompletedTask;
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await ApiErrors.Problem(401, "unauthenticated", context.HttpContext).ExecuteAsync(context.HttpContext);
                },
                OnForbidden = context => ApiErrors.Problem(403, "forbidden", context.HttpContext).ExecuteAsync(context.HttpContext)
            };
        });
        services.AddAuthorization(options => options.FallbackPolicy =
            new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }
}
