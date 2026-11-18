using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MiniWallet.Wallet.Tests.Integration;

public sealed class MediatorDispatchTests(WalletDatabase database) : IClassFixture<WalletDatabase>
{
    [Fact]
    public async Task Wallet_endpoints_dispatch_through_mediator_and_keep_the_http_contract()
    {
        var owner = await database.SeedAsync(100);
        var recorder = new Recorder();
        using var factory = new WalletApiFactory(database.ConnectionString);
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => RegisterProbe(services, recorder)));
        using var client = app.CreateClient();
        using var own = await Get(client, "/api/v1/wallets/me", owner.Subject);
        using var admin = await Get(client, $"/api/v1/players/{owner.Id}/wallets", "admin", true);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        using var bet = await TransactionTests.Post(client, new { type = "BET", currency = "USD", amount = "10" }, "bet", owner.Subject);
        Assert.Equal(HttpStatusCode.Created, bet.StatusCode);
        var betId = (await bet.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transactionId").GetGuid();
        using var history = await Get(client, "/api/v1/transactions/me", owner.Subject);
        using var adminHistory = await Get(client, $"/api/v1/players/{owner.Id}/transactions", "admin", true);
        using var single = await Get(client, $"/api/v1/transactions/{betId}", owner.Subject);
        foreach (var response in new[] { history, adminHistory, single }) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var bonus = await TransactionTests.Post(client, new { type = "BONUS", playerId = owner.Id, currency = "USD", amount = "5" }, "bonus", "admin", true);
        using var win = await TransactionTests.Post(client, new { type = "WIN", betId }, "win", "admin", true);
        Assert.Equal(HttpStatusCode.Created, bonus.StatusCode);
        Assert.Equal(HttpStatusCode.Created, win.StatusCode);
        Assert.Equal(115m, await database.BalanceAsync(owner.Id));
        Assert.Equal(new[] { "GetBalancesQuery", "GetBalancesQuery", "ExecuteTransactionCommand", "GetTransactionHistoryQuery",
            "GetTransactionHistoryQuery", "GetTransactionQuery", "ExecuteTransactionCommand", "ExecuteTransactionCommand" }, recorder.Requests.ToArray());
        var swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        Assert.EndsWith("/TransactionRequest", swagger.GetProperty("paths").GetProperty("/api/v1/transactions").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
    }

    [Theory]
    [InlineData("WIN", HttpStatusCode.Forbidden, "forbidden")]
    [InlineData("BET", HttpStatusCode.BadRequest, "invalid_request")]
    public async Task Rejected_transactions_keep_permission_and_validation_order(string type, HttpStatusCode status, string code)
    {
        var recorder = new Recorder();
        using var factory = new WalletApiFactory(database.ConnectionString);
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => RegisterProbe(services, recorder)));
        using var client = app.CreateClient();
        using var response = await TransactionTests.Post(client, new { type }, "invalid", "player");
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(new[] { "ExecuteTransactionCommand" }, recorder.Requests.ToArray());
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string path, string subject, bool admin = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", WalletApiFactory.Token(subject, admin));
        return await client.SendAsync(request);
    }

    private static void RegisterProbe(IServiceCollection services, Recorder recorder)
    {
        services.AddSingleton(recorder);
        services.AddHttpContextAccessor();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(Probe<,>));
    }

    public sealed class Recorder
    {
        public ConcurrentQueue<string> Requests { get; } = new();
    }

    public sealed class Probe<TRequest, TResponse>(Recorder recorder, IHttpContextAccessor http)
        : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            Assert.Equal(http.HttpContext!.RequestAborted, cancellationToken);
            recorder.Requests.Enqueue(typeof(TRequest).Name);
            return next();
        }
    }
}
