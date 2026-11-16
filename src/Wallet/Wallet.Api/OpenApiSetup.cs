using Microsoft.OpenApi;
namespace MiniWallet.Wallet.Api;

public static class OpenApiSetup
{
    public static void AddApiDocumentation(this IServiceCollection services, IConfiguration configuration)
    {
        var issuer = configuration["Auth:PublicIssuer"] ?? "http://localhost:8080/realms/mini-wallet";
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.OperationFilter<TransactionExamples>();
            options.SchemaFilter<TransactionSchema>();
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Mini Wallet — Wallet", Version = "v1",
                Description = "Sign in with OAuth code + PKCE. Use player for owned operations and admin for administration. All money amounts are decimal strings." });
            options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri(issuer + "/protocol/openid-connect/auth"),
                        TokenUrl = new Uri(issuer + "/protocol/openid-connect/token"),
                        Scopes = new Dictionary<string, string> { ["openid"] = "OpenID Connect", ["profile"] = "Profile", ["email"] = "Verified email" }
                    }
                }
            });
            options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
                Description = "Alternatively paste the same access token used in the other API. Use only one authorization method at a time."
            });
            options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
                { [new OpenApiSecuritySchemeReference("oauth2", doc)] = ["openid", "profile", "email"] });
            options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
                { [new OpenApiSecuritySchemeReference("bearer", doc)] = [] });
        });
        services.AddCors(options => options.AddPolicy("web", policy =>
            policy.WithOrigins(configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:3000"])
                .AllowAnyMethod().AllowAnyHeader().AllowCredentials().WithExposedHeaders("Location", "Retry-After")));
    }
    public static void UseApiDocumentation(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Wallet v1");
            options.OAuthClientId("mini-wallet-wallet-swagger");
            options.OAuthUsePkce();
            options.OAuthScopes("openid", "profile", "email");
            options.OAuthAdditionalQueryStringParams(new Dictionary<string, string> { ["prompt"] = "login" });
            options.DisplayRequestDuration();
        });
    }
}
