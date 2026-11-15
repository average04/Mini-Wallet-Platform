using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace MiniWallet.Wallet.Tests.Integration;

public sealed class BalanceHubTests(WalletDatabase database) : IClassFixture<WalletDatabase>
{
    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    public async Task Invalid_token_cannot_negotiate_a_balance_feed(string reason)
    {
        using var factory = new WalletApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        var token = reason switch
        {
            "expired" => WalletApiFactory.Token(expired: true),
            "issuer" => WalletApiFactory.Token(issuer: "https://wrong.example"),
            "audience" => WalletApiFactory.Token(audience: "another-api"),
            _ => ""
        };
        if (token.Length > 0) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.PostAsync("/hubs/balances/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Browser_websocket_can_authenticate_using_a_query_token()
    {
        var player = await database.SeedAsync();
        using var factory = new WalletApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await factory.Server.CreateWebSocketClient().ConnectAsync(
            new Uri("ws://localhost/hubs/balances?access_token=" + WalletApiFactory.Token(player.Subject)), timeout.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[4096];
        var received = "";
        while (!received.Contains("Ready", StringComparison.Ordinal))
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
            Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
            received += Encoding.UTF8.GetString(buffer, 0, result.Count);
        }
        Assert.Contains("\"target\":\"Ready\"", received);
    }

    [Fact]
    public async Task Browser_query_token_authenticates_on_real_Kestrel()
    {
        var player = await database.SeedAsync();
        using var factory = new WalletApiFactory(database.ConnectionString);
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = new ClientWebSocket();
        var uri = new UriBuilder(client.BaseAddress!)
        {
            Scheme = "ws", Path = "/hubs/balances",
            Query = "access_token=" + Uri.EscapeDataString(WalletApiFactory.Token(player.Subject))
        }.Uri;
        await socket.ConnectAsync(uri, timeout.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[4096];
        var received = "";
        while (!received.Contains("Ready", StringComparison.Ordinal))
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
            Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
            received += Encoding.UTF8.GetString(buffer, 0, result.Count);
        }
        Assert.Contains("\"target\":\"Ready\"", received);
    }

    [Fact]
    public async Task Unprovisioned_identity_cannot_open_a_feed()
    {
        using var factory = new WalletApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Feed.ConnectAsync(factory, "no-profile"));
        Assert.IsNotType<TimeoutException>(error);
    }

    [Fact]
    public async Task Query_player_id_cannot_subscribe_to_another_players_balance()
    {
        var owner = await database.SeedAsync();
        var other = await database.SeedAsync();
        using var factory = new WalletApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        await using var feed = await Feed.ConnectAsync(factory, owner.Subject, query: "?playerId=" + other.Id);
        using var bonus = await TransactionTests.Post(client, new { type = "BONUS", playerId = other.Id, currency = "USD", amount = "10" }, "private", "admin", true);
        Assert.Equal(HttpStatusCode.Created, bonus.StatusCode);
        await feed.AssertQuietAsync();
    }

    [Fact]
    public async Task Feed_closes_when_its_access_token_expires()
    {
        var player = await database.SeedAsync();
        using var factory = new WalletApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        await using var feed = await Feed.ConnectAsync(factory, player.Subject,
            WalletApiFactory.Token(player.Subject, expiresAt: DateTime.UtcNow.AddSeconds(4)));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        feed.Connection.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HubConnectionState.Disconnected, feed.Connection.State);
    }

    [Fact]
    public async Task Authenticated_player_can_negotiate_the_balance_hub()
    {
        using var factory = new WalletApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", WalletApiFactory.Token());
        using var response = await client.PostAsync("/hubs/balances/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("connectionToken", out _));
    }

    [Fact]
    public async Task Committed_bet_notifies_only_its_owner_and_replay_or_rejection_does_not_notify()
    {
        var owner = await database.SeedAsync(100);
        var other = await database.SeedAsync(100);
        using var factory = new WalletApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        await using var ownFeed = await Feed.ConnectAsync(factory, owner.Subject);
        await using var otherFeed = await Feed.ConnectAsync(factory, other.Subject);
        using var bet = await TransactionTests.Post(client, new { type = "BET", currency = "USD", amount = "10" }, "live-bet", owner.Subject);
        Assert.Equal(HttpStatusCode.Created, bet.StatusCode);
        var transaction = await bet.Content.ReadFromJsonAsync<JsonElement>();
        var notification = await ownFeed.NextAsync();
        Assert.Equal(transaction.GetProperty("transactionId").GetGuid(), notification.GetProperty("transactionId").GetGuid());
        Assert.Equal("USD", notification.GetProperty("currency").GetString());
        Assert.Equal("90", notification.GetProperty("balanceAfter").GetString());
        Assert.Equal(90m, await database.BalanceAsync(owner.Id));
        using var replay = await TransactionTests.Post(client, new { type = "BET", currency = "USD", amount = "10" }, "live-bet", owner.Subject);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        using var denied = await TransactionTests.Post(client, new { type = "BET", currency = "USD", amount = "1000" }, "rejected", owner.Subject);
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        await ownFeed.AssertQuietAsync();
        await otherFeed.AssertQuietAsync();
    }

    [Fact]
    public async Task Admin_bonus_and_win_notify_the_player_whose_balance_changed()
    {
        var owner = await database.SeedAsync();
        using var factory = new WalletApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        await using var feed = await Feed.ConnectAsync(factory, owner.Subject);
        using var bonus = await TransactionTests.Post(client, new { type = "BONUS", playerId = owner.Id, currency = "USD", amount = "100" }, "live-bonus", "admin", true);
        Assert.Equal(HttpStatusCode.Created, bonus.StatusCode);
        Assert.Equal("100", (await feed.NextAsync()).GetProperty("balanceAfter").GetString());
        using var bet = await TransactionTests.Post(client, new { type = "BET", currency = "USD", amount = "10" }, "live-bet", owner.Subject);
        var betJson = await bet.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("90", (await feed.NextAsync()).GetProperty("balanceAfter").GetString());
        using var win = await TransactionTests.Post(client, new { type = "WIN", betId = betJson.GetProperty("transactionId").GetGuid(), amount = "15" }, "live-win", "admin", true);
        Assert.Equal(HttpStatusCode.Created, win.StatusCode);
        Assert.Equal("105", (await feed.NextAsync()).GetProperty("balanceAfter").GetString());
    }

    [Theory]
    [InlineData("/api/v1/wallets/me", true)]
    [InlineData("/hubs/balances-other", true)]
    [InlineData("/hubs/balances", false)]
    public async Task Query_token_cannot_authenticate_other_routes(string route, bool eventStream)
    {
        using var factory = new WalletApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, route + "?access_token=" + WalletApiFactory.Token());
        if (eventStream) request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Query_token_cannot_authenticate_negotiation()
    {
        using var factory = new WalletApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/hubs/balances/negotiate?negotiateVersion=1&access_token=" + WalletApiFactory.Token(), null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    internal sealed class Feed : IAsyncDisposable
    {
        public HubConnection Connection { get; }
        private readonly Channel<JsonElement> events = Channel.CreateUnbounded<JsonElement>();
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Feed(WalletApiFactory factory, string token, string? query = null)
        {
            Connection = new HubConnectionBuilder().WithUrl("http://localhost/hubs/balances" + query, options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            }).Build();
            Connection.On("Ready", () => ready.TrySetResult());
            Connection.On<JsonElement>("BalanceChanged", value => events.Writer.TryWrite(value));
            Connection.Closed += error => { ready.TrySetException(error ?? new InvalidOperationException("Hub closed before Ready")); return Task.CompletedTask; };
        }

        public static async Task<Feed> ConnectAsync(WalletApiFactory factory, string subject, string? token = null, string? query = null)
        {
            var feed = new Feed(factory, token ?? WalletApiFactory.Token(subject), query);
            try
            {
                await feed.Connection.StartAsync();
                await feed.ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return feed;
            }
            catch { await feed.DisposeAsync(); throw; }
        }

        public async Task<JsonElement> NextAsync() => await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        public async Task AssertQuietAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await events.Reader.ReadAsync(timeout.Token));
        }
        public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
    }
}
