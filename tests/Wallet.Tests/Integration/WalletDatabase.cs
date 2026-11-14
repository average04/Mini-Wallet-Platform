using Microsoft.EntityFrameworkCore;
using MiniWallet.Wallet.Domain;
using MiniWallet.Wallet.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace MiniWallet.Wallet.Tests.Integration;

public sealed class WalletDatabase : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder("postgres:17.9").Build();
    public string ConnectionString => Container.GetConnectionString();
    public WalletDbContext Open() => new(new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(ConnectionString).Options);
    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        await using var db = Open();
        await db.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();

    public async Task<(Guid Id, string Subject)> SeedAsync(decimal dollars = 0)
    {
        var id = Guid.NewGuid();
        var subject = id.ToString();
        await using var db = Open();
        db.Identities.Add(new IdentityAssociation { Issuer = WalletApiFactory.Issuer, Subject = subject, PlayerId = id });
        foreach (var currency in new[] { "USD", "EUR", "BTC" })
        {
            var wallet = new BalanceAccount(id, currency);
            if (currency == "USD" && dollars > 0)
            {
                wallet.Credit(Money.Parse("USD", dollars.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                db.Transactions.Add(new LedgerEntry { Id = Guid.NewGuid(), PlayerId = id, Currency = currency,
                    Kind = "BONUS", Amount = dollars, BalanceAfter = dollars, ActorIssuer = WalletApiFactory.Issuer,
                    ActorSubject = "fixture-admin", CreatedAt = DateTimeOffset.UtcNow });
            }
            db.Wallets.Add(wallet);
        }
        await db.SaveChangesAsync();
        return (id, subject);
    }

    public async Task<decimal> BalanceAsync(Guid playerId, string currency = "USD")
    {
        await using var db = Open();
        return await db.Wallets.Where(w => w.PlayerId == playerId && w.Currency == currency).Select(w => w.Balance).SingleAsync();
    }
    public async Task<int> CountAsync(Guid playerId, string kind)
    {
        await using var db = Open();
        return await db.Transactions.CountAsync(t => t.PlayerId == playerId && t.Kind == kind);
    }
}
