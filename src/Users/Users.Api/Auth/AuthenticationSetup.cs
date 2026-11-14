using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace MiniWallet.Users.Api.Auth;

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
        services.AddAuthorization();
        return services;
    }
}
