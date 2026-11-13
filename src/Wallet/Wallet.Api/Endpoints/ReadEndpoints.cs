using MiniWallet.Wallet.Api.Auth;
using MiniWallet.Wallet.Application.Features.Reads;
using MiniWallet.Wallet.Application.Features.Transactions;
namespace MiniWallet.Wallet.Api.Endpoints;

public static class ReadEndpoints
{
    public static void MapWalletReads(this WebApplication app)
    {
        var balances = app.MapGroup("/api/v1").RequireAuthorization().WithTags("Balances");
        balances.MapGet("/wallets/me", async (HttpContext context, WalletQueries query, CancellationToken ct) =>
            Results.Ok(await query.BalancesAsync(ActorMapper.Map(context.User), null, ct)))
            .WithSummary("Read your USD, EUR and BTC wallets")
            .Produces<IReadOnlyList<BalanceView>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(409).ProducesProblem(503);
        balances.MapGet("/players/{playerId:guid}/wallets", async (Guid playerId, HttpContext context, WalletQueries query, CancellationToken ct) =>
            Results.Ok(await query.BalancesAsync(ActorMapper.Map(context.User), playerId, ct)))
            .WithSummary("Admin: read a player's wallets")
            .Produces<IReadOnlyList<BalanceView>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(503);
        var history = app.MapGroup("/api/v1").RequireAuthorization().WithTags("Transactions");
        history.MapGet("/transactions/me", async (int? limit, string? cursor, HttpContext context, WalletQueries query, CancellationToken ct) =>
            Results.Ok(await query.HistoryAsync(ActorMapper.Map(context.User), null, limit, cursor, ct)))
            .WithSummary("Read your successful transaction history")
            .WithDescription("Newest first by createdAt and transactionId. Default limit 20, maximum 100. Pass nextCursor unchanged for the next page. Rejected attempts are excluded.")
            .Produces<HistoryPage>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409).ProducesProblem(503);
        history.MapGet("/players/{playerId:guid}/transactions", async (Guid playerId, int? limit, string? cursor, HttpContext context, WalletQueries query, CancellationToken ct) =>
            Results.Ok(await query.HistoryAsync(ActorMapper.Map(context.User), playerId, limit, cursor, ct)))
            .WithSummary("Admin: read a player's successful transaction history")
            .Produces<HistoryPage>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(503);
        history.MapGet("/transactions/{transactionId:guid}", async (Guid transactionId, HttpContext context, WalletQueries query, CancellationToken ct) =>
            Results.Ok(await query.TransactionAsync(ActorMapper.Map(context.User), transactionId, ct)))
            .WithSummary("Read one successful transaction as owner or admin")
            .Produces<TransactionView>().ProducesProblem(401).ProducesProblem(404).ProducesProblem(503);
    }
}
