using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MiniWallet.Wallet.Api.Auth;
using MiniWallet.Wallet.Application.Features.Transactions;
using MiniWallet.Wallet.Infrastructure;

namespace MiniWallet.Wallet.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddPlatformAuthentication(builder.Configuration, builder.Environment);
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
        builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
        builder.Services.AddScoped<ExecuteTransaction>();
        builder.Services.AddScoped<TransactionRequestValidator>();
        builder.Services.AddScoped<ITransactionHandler, BetHandler>();
        builder.Services.AddScoped<ITransactionHandler, WinHandler>();
        builder.Services.AddScoped<ITransactionHandler, BonusHandler>();
        builder.Services.AddWalletInfrastructure();
        var app = builder.Build();
        app.UseApiErrors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapPost("/api/v1/transactions", async (TransactionRequest request,
            [FromHeader(Name = "Idempotency-Key")] string key, HttpContext context,
            ExecuteTransaction execute, CancellationToken cancellationToken) =>
            {
                var outcome = await execute.ExecuteAsync(ActorMapper.Map(context.User), request, key, cancellationToken);
                return outcome.Transaction is { } transaction
                    ? Results.Created($"/api/v1/transactions/{transaction.TransactionId}", transaction)
                    : ApiErrors.Problem(409, outcome.Code!, context);
            })
            .RequireAuthorization();
        app.Run();
    }
}

