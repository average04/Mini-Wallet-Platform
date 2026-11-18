using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using MiniWallet.Wallet.Api.Auth;
using MiniWallet.Wallet.Api.Endpoints;
using MiniWallet.Wallet.Api.Realtime;
using MiniWallet.Wallet.Application;
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
        builder.Services.AddWalletApplication();
        builder.Services.AddWalletInfrastructure();
        builder.Services.AddApiDocumentation(builder.Configuration);
        builder.Services.AddBalanceUpdates(builder.Configuration);
        var app = builder.Build();
        if (migrate)
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<WalletDbContext>().Database.MigrateAsync();
            return;
        }
        app.UseApiErrors();
        app.UseApiDocumentation();
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            app.UseStaticFiles();
            app.MapGet("/live-balances", () => Results.Redirect("/live-balances/index.html"))
                .AllowAnonymous().ExcludeFromDescription();
        }
        app.UseCors("web");
        // Expose the WebSocket request feature before JWT's browser query-token check.
        app.UseWebSockets();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous().ExcludeFromDescription();
        app.MapWalletReads();
        app.MapHub<BalanceHub>(BalanceHub.Path, options => options.CloseOnAuthenticationExpiration = true)
            .RequireAuthorization();
        app.MapPost("/api/v1/transactions", async (TransactionRequest request,
            [FromHeader(Name = "Idempotency-Key")] string key, HttpContext context,
            ISender sender, BalanceUpdateNotifier updates, CancellationToken cancellationToken) =>
            {
                var execution = await sender.Send(new ExecuteTransactionCommand(ActorMapper.Map(context.User), request, key), cancellationToken);
                var outcome = execution.Outcome;
                if (!execution.IsReplay && outcome.Transaction is { } changed)
                    await updates.PublishAsync(changed);
                return outcome.Transaction is { } transaction
                    ? Results.Created($"/api/v1/transactions/{transaction.TransactionId}", transaction)
                    : ApiErrors.Problem(409, outcome.Code!, context);
            })
            .RequireAuthorization().WithTags("Transactions")
            .WithSummary("Record a BET, WIN or BONUS")
            .WithDescription("""
                | Type | Who | Required fields | Optional fields |
                | --- | --- | --- | --- |
                | BET | Player | `type`, `currency`, `amount` | None |
                | WIN | Admin | `type`, `betId` | `multiplier` (default `"2"`), `bonusAmount` (default `"0"`) |
                | BONUS | Admin | `type`, `playerId`, `currency`, `amount` | None |

                **WIN payout = original bet amount × multiplier + bonusAmount.** A 10-unit bet at 2x with a 5-unit bonus credits 25. The multiplier includes the stake; the bonus is added afterward. WIN derives player and currency from the bet. Its response `amount` is the calculated total; do not send input `amount` for WIN.

                **Validation**
                - Send money and multipliers as JSON strings, for example `"10.50"`, not JSON numbers.
                - BET/BONUS amounts must be positive. USD/EUR allow 2 fractional digits; BTC allows 8. Currency must be `USD`, `EUR` or `BTC`.
                - WIN multiplier must be positive with at most 8 fractional digits. Bonus must be zero or positive and obey the bet's currency precision. Each decimal string is at most 64 characters.
                - No signs, spaces, exponents or thousands separators. Fractional currency units and out-of-range payouts are rejected, never rounded.
                - Required IDs must be nonempty UUIDs. Omit fields belonging to other transaction types. Unknown properties are rejected.
                - `Idempotency-Key` is required: 1–128 ASCII letters, digits, `-`, `_`, `.` or `:`. It is scoped to the target player. Equivalent retries return the original result; changing WIN multiplier or bonus with the same key returns 409.

                Invalid input returns **400 Problem Details** with `code` and `traceId`; see the response examples. Insufficient funds, an already settled bet, or exceeding the wallet balance limit returns **409**.
                """)
            .Produces<TransactionView>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        await app.RunAsync();
    }
}
