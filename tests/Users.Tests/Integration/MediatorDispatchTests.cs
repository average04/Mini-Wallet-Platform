using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MiniWallet.Users.Tests.Integration;

public sealed class MediatorDispatchTests(UsersDatabase database) : IClassFixture<UsersDatabase>
{
    [Fact]
    public async Task Profile_endpoints_dispatch_through_mediator_with_the_request_cancellation_token()
    {
        var recorder = new Recorder();
        using var factory = new UsersApiFactory(database.ConnectionString);
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => RegisterProbe(services, recorder)));
        using var client = app.CreateClient();
        var subject = Guid.NewGuid().ToString();
        using var created = await ProfileApiTests.Send(client, HttpMethod.Post, "/api/v1/profiles", subject, new { displayName = " Mediator Player " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
        using var own = await ProfileApiTests.Send(client, HttpMethod.Get, "/api/v1/profiles/me", subject);
        using var updated = await ProfileApiTests.Send(client, HttpMethod.Put, "/api/v1/profiles/me", subject, new { displayName = "Updated" });
        using var adminRead = await ProfileApiTests.Send(client, HttpMethod.Get, $"/api/v1/profiles/{id}", "admin", admin: true);
        using var adminUpdate = await ProfileApiTests.Send(client, HttpMethod.Put, $"/api/v1/profiles/{id}", "admin", new { displayName = "Admin updated" }, admin: true);
        foreach (var response in new[] { own, updated, adminRead, adminUpdate }) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "CreateProfileCommand", "GetProfileQuery", "UpdateProfileCommand", "GetProfileQuery", "UpdateProfileCommand" }, recorder.Requests.ToArray());
        var swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        Assert.EndsWith("/ProfileRequest", swagger.GetProperty("paths").GetProperty("/api/v1/profiles").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
    }

    [Theory]
    [InlineData(false, false, HttpStatusCode.Forbidden, "email_not_verified", "CreateProfileCommand")]
    [InlineData(true, false, HttpStatusCode.BadRequest, "invalid_display_name", "CreateProfileCommand")]
    [InlineData(true, true, HttpStatusCode.Forbidden, "forbidden", "UpdateProfileCommand")]
    public async Task Rejected_commands_keep_authorization_before_validation(bool verified, bool updateOther,
        HttpStatusCode expected, string code, string command)
    {
        var recorder = new Recorder();
        using var factory = new UsersApiFactory(database.ConnectionString);
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => RegisterProbe(services, recorder)));
        using var client = app.CreateClient();
        using var response = await ProfileApiTests.Send(client, updateOther ? HttpMethod.Put : HttpMethod.Post,
            updateOther ? $"/api/v1/profiles/{Guid.NewGuid()}" : "/api/v1/profiles", "player", new { displayName = "" }, verified: verified);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(new[] { command }, recorder.Requests.ToArray());
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
