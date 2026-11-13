using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MiniWallet.Contracts;
using MiniWallet.Users.Infrastructure.Persistence;
using Npgsql;

namespace MiniWallet.Users.Tests.Integration;

public sealed class ProfileApiTests(UsersDatabase database) : IClassFixture<UsersDatabase>
{
    internal static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path,
        string subject, object? body = null, bool admin = false, bool verified = true)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", UsersApiFactory.Token(subject, admin, verified: verified));
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    private static Task<HttpResponseMessage> Create(HttpClient client, string subject, string name = "Player", bool verified = true)
        => Send(client, HttpMethod.Post, "/api/v1/profiles", subject, new { displayName = name }, verified: verified);

    private async Task<int> EventCount(Guid playerId)
    {
        await using var db = database.Open();
        var events = await db.Outbox.AsNoTracking().ToListAsync();
        return events.Count(e => JsonSerializer.Deserialize<PlayerCreatedV1>(e.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.PlayerId == playerId);
    }

    [Fact]
    public async Task Player_can_create_read_and_update_own_profile_with_one_durable_event()
    {
        var subject = Guid.NewGuid().ToString();
        using var app = new UsersApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var created = await Create(client, subject, " Alice ");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/v1/profiles/me", created.Headers.Location?.ToString());
        var profile = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = profile.GetProperty("playerId").GetGuid();
        Assert.Equal("Alice", profile.GetProperty("displayName").GetString());
        using var read = await Send(client, HttpMethod.Get, "/api/v1/profiles/me", subject);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var update = await Send(client, HttpMethod.Put, "/api/v1/profiles/me", subject, new { displayName = " Bob " });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Bob", (await update.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("displayName").GetString());
        Assert.Equal(1, await EventCount(id));
        await using var db = database.Open();
        var association = await db.Identities.SingleAsync(i => i.Subject == subject);
        Assert.Equal(id, association.PlayerId);
        var persistedEvent = (await db.Outbox.ToListAsync()).Single(e =>
            JsonSerializer.Deserialize<PlayerCreatedV1>(e.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.PlayerId == id);
        Assert.Null(persistedEvent.PublishedAt);
    }

    [Fact]
    public async Task Repeated_create_returns_existing_profile_without_edit_or_another_event()
    {
        var subject = Guid.NewGuid().ToString();
        using var app = new UsersApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var first = await Create(client, subject, "Original");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
        using var again = await Create(client, subject, "Different");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var profile = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, profile.GetProperty("playerId").GetGuid());
        Assert.Equal("Original", profile.GetProperty("displayName").GetString());
        Assert.Equal(1, await EventCount(id));
    }

    [Fact]
    public async Task Concurrent_creates_across_hosts_produce_one_profile_and_one_event()
    {
        var subject = Guid.NewGuid().ToString();
        using var a = new UsersApiFactory(database.ConnectionString); using var b = new UsersApiFactory(database.ConnectionString);
        using var ca = a.CreateClient(); using var cb = b.CreateClient();
        var responses = await Task.WhenAll(Create(ca, subject), Create(cb, subject));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var first = (await responses[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
        var second = (await responses[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
        Assert.Equal(first, second);
        await using var db = database.Open();
        Assert.Equal(1, await db.Identities.CountAsync(i => i.Subject == subject));
        Assert.Equal(1, await EventCount(first));
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task Lost_create_response_can_be_retried_without_an_idempotency_key()
    {
        var subject = Guid.NewGuid().ToString();
        using var app = new UsersApiFactory(database.ConnectionString); using var client = app.CreateClient();
        (await Create(client, subject)).Dispose();
        using var retry = await Create(client, subject);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var id = (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
        Assert.Equal(1, await EventCount(id));
    }

    [Fact]
    public async Task Unverified_identity_cannot_create_a_profile()
    {
        var subject = Guid.NewGuid().ToString();
        using var app = new UsersApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var response = await Create(client, subject, verified: false);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var db = database.Open();
        Assert.False(await db.Identities.AnyAsync(i => i.Subject == subject));
    }

    [Fact]
    public async Task Admin_without_a_profile_can_read_and_update_another_profile()
    {
        var subject = Guid.NewGuid().ToString();
        using var app = new UsersApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var created = await Create(client, subject); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
        using var read = await Send(client, HttpMethod.Get, $"/api/v1/profiles/{id}", "admin-without-profile", admin: true);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var updated = await Send(client, HttpMethod.Put, $"/api/v1/profiles/{id}", "admin-without-profile",
            new { displayName = "Admin edit" }, admin: true);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Admin edit", (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Player_cannot_read_or_update_a_foreign_profile()
    {
        var owner = Guid.NewGuid().ToString();
        using var app = new UsersApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var created = await Create(client, owner); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
        using var read = await Send(client, HttpMethod.Get, $"/api/v1/profiles/{id}", "intruder");
        using var update = await Send(client, HttpMethod.Put, $"/api/v1/profiles/{id}", "intruder", new { displayName = "Stolen" });
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        using var ownMissing = await Send(client, HttpMethod.Get, "/api/v1/profiles/me", "intruder");
        Assert.Equal(HttpStatusCode.NotFound, ownMissing.StatusCode);
    }

    [Fact]
    public async Task Profile_update_cannot_change_roles_or_external_identity()
    {
        var subject = Guid.NewGuid().ToString();
        using var app = new UsersApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var created = await Create(client, subject); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var response = await Send(client, HttpMethod.Put, "/api/v1/profiles/me", subject,
            new { displayName = "Exploit", roles = new[] { "admin" }, issuer = "other" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var read = await Send(client, HttpMethod.Get, "/api/v1/profiles/me", subject);
        Assert.Equal("Player", (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("displayName").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Invalid_display_name_is_rejected_without_creating_identity(string name)
    {
        var subject = Guid.NewGuid().ToString();
        using var app = new UsersApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var response = await Create(client, subject, name);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = database.Open();
        Assert.False(await db.Identities.AnyAsync(i => i.Subject == subject));
    }

    [Fact]
    public async Task Outbox_failure_rolls_back_profile_and_identity_before_retry()
    {
        var subject = Guid.NewGuid().ToString();
        using var app = new UsersApiFactory(database.ConnectionString); using var client = app.CreateClient();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var constraint = "test_outbox_" + Guid.NewGuid().ToString("N");
        await using var add = new NpgsqlCommand($"ALTER TABLE outbox ADD CONSTRAINT {constraint} CHECK (payload_json->>'subject' <> '{subject}')", connection);
        await add.ExecuteNonQueryAsync();
        try
        {
            using var failed = await Create(client, subject, subject);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            await using var db = database.Open();
            Assert.False(await db.Profiles.AnyAsync(p => p.DisplayName == subject));
            Assert.False(await db.Identities.AnyAsync(i => i.Subject == subject));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"ALTER TABLE outbox DROP CONSTRAINT {constraint}", connection);
            await drop.ExecuteNonQueryAsync();
        }
        using var retry = await Create(client, subject, subject);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var id = (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
        Assert.Equal(1, await EventCount(id));
    }

    [Fact]
    public async Task Another_verified_association_can_preserve_the_platform_player_id()
    {
        var subject = Guid.NewGuid().ToString(); var alternate = Guid.NewGuid().ToString();
        using var app = new UsersApiFactory(database.ConnectionString); using var client = app.CreateClient();
        using var created = await Create(client, subject); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
        await using (var db = database.Open())
        {
            // Test fixture for an independently verified migration; no email-based automatic linking.
            db.Identities.Add(new IdentityAssociation { Issuer = UsersApiFactory.Issuer, Subject = alternate, PlayerId = id });
            await db.SaveChangesAsync();
        }
        using var read = await Send(client, HttpMethod.Get, "/api/v1/profiles/me", alternate);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(id, (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid());
    }
}
