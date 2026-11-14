using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MiniWallet.Wallet.Api.Auth;
using MiniWallet.Wallet.Api.Endpoints;
using MiniWallet.Wallet.Application.Features.Reads;
using MiniWallet.Wallet.Application.Features.Transactions;
using MiniWallet.Wallet.Infrastructure;
using MiniWallet.Wallet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MiniWallet.Wallet.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var migrate = args.Contains("--migrate");
        var builder = WebApplication.CreateBuilder(args.Where(a => a != "--migrate").ToArray());
        builder.Services.AddPlatformAuthentication(builder.Configuration, builder.Environment);
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
        builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
        builder.Services.AddScoped<ExecuteTransaction>();
        builder.Services.AddScoped<WalletQueries>();
        builder.Services.AddScoped<TransactionRequestValidator>();
        builder.Services.AddScoped<ITransactionHandler, BetHandler>();
        builder.Services.AddScoped<ITransactionHandler, WinHandler>();
        builder.Services.AddScoped<ITransactionHandler, BonusHandler>();
        builder.Services.AddWalletInfrastructure();
        builder.Services.AddApiDocumentation(builder.Configuration);
        var app = builder.Build();
        if (migrate)
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<WalletDbContext>().Database.MigrateAsync();
            return;
        }
        app.UseApiErrors();
        app.UseApiDocumentation();
        app.UseCors("web");
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous().ExcludeFromDescription();
        app.MapWalletReads();
        app.MapPost("/api/v1/transactions", async (TransactionRequest request,
            [FromHeader(Name = "Idempotency-Key")] string key, HttpContext context,
            ExecuteTransaction execute, CancellationToken cancellationToken) =>
            {
                var outcome = await execute.ExecuteAsync(ActorMapper.Map(context.User), request, key, cancellationToken);
                return outcome.Transaction is { } transaction
                    ? Results.Created($"/api/v1/transactions/{transaction.TransactionId}", transaction)
                    : ApiErrors.Problem(409, outcome.Code!, context);
            })
            .RequireAuthorization().WithTags("Transactions")
            .WithSummary("Record a BET, WIN or BONUS")
            .WithDescription("BET: type, currency, amount. WIN (admin): type, betId, amount. BONUS (admin): type, playerId, currency, amount. Omit fields belonging to other types. Idempotency-Key is scoped to the target player; replay returns the original result.")
            .Produces<TransactionView>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        await app.RunAsync();
    }
}
