using Microsoft.EntityFrameworkCore;
using MiniWallet.Users.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace MiniWallet.Users.Tests.Integration;

public sealed class UsersDatabase : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder("postgres:17.9").Build();
    public string ConnectionString => Container.GetConnectionString();
    public UsersDbContext Open() => new(new DbContextOptionsBuilder<UsersDbContext>().UseNpgsql(ConnectionString).Options);
    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        await using var db = Open();
        await db.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}
