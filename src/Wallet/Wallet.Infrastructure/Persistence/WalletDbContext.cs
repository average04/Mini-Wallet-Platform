using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MiniWallet.Wallet.Domain;

namespace MiniWallet.Wallet.Infrastructure.Persistence;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public DbSet<BalanceAccount> Wallets => Set<BalanceAccount>();
    public DbSet<LedgerEntry> Transactions => Set<LedgerEntry>();
    public DbSet<IdentityAssociation> Identities => Set<IdentityAssociation>();
    public DbSet<IdempotencyRecord> Idempotency => Set<IdempotencyRecord>();
    public DbSet<InboxRecord> Inbox => Set<InboxRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<BalanceAccount>(b =>
        {
            b.ToTable("wallets", t =>
            {
                t.HasCheckConstraint("ck_wallet_nonnegative", "balance >= 0");
                t.HasCheckConstraint("ck_wallet_currency", "currency IN ('USD','EUR','BTC')");
            });
            b.HasKey(x => new { x.PlayerId, x.Currency });
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.Balance).HasPrecision(28, 8);
        });
        model.Entity<LedgerEntry>(b =>
        {
            b.ToTable("transactions", t =>
            {
                t.HasCheckConstraint("ck_transaction_positive", "amount > 0");
                t.HasCheckConstraint("ck_transaction_kind", "kind IN ('BET','WIN','BONUS')");
                t.HasCheckConstraint("ck_transaction_bet", "(kind = 'WIN' AND bet_id IS NOT NULL) OR (kind <> 'WIN' AND bet_id IS NULL)");
                t.HasCheckConstraint("ck_transaction_balance", "balance_after >= 0");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Amount).HasPrecision(28, 8);
            b.Property(x => x.BalanceAfter).HasPrecision(28, 8);
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.Kind).HasMaxLength(10);
            b.Property(x => x.ActorIssuer).HasMaxLength(500);
            b.Property(x => x.ActorSubject).HasMaxLength(200);
            b.HasOne<BalanceAccount>().WithMany().HasForeignKey(x => new { x.PlayerId, x.Currency }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LedgerEntry>().WithMany().HasForeignKey(x => x.BetId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.BetId).IsUnique().HasFilter("kind = 'WIN'");
            b.HasIndex(x => new { x.PlayerId, x.CreatedAt, x.Id }).IsDescending(false, true, true);
        });
        model.Entity<IdentityAssociation>(b =>
        {
            b.ToTable("player_identities");
            b.HasKey(x => new { x.Issuer, x.Subject });
            b.Property(x => x.Issuer).HasMaxLength(500);
            b.Property(x => x.Subject).HasMaxLength(200);
            b.HasIndex(x => x.PlayerId);
        });
        model.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("idempotency");
            b.HasKey(x => new { x.PlayerId, x.Scope, x.Key });
            b.Property(x => x.Scope).HasMaxLength(64);
            b.Property(x => x.Key).HasMaxLength(128);
            b.Property(x => x.Fingerprint).HasMaxLength(64);
            b.Property(x => x.ResponseJson).HasColumnType("jsonb");
        });
        model.Entity<InboxRecord>(b =>
        {
            b.ToTable("inbox");
            b.HasKey(x => x.EventId);
            b.Property(x => x.EventId).ValueGeneratedNever();
            b.Property(x => x.PayloadHash).HasMaxLength(64);
        });
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name));
    }
}

public sealed class IdentityAssociation
{
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public Guid PlayerId { get; set; }
}
public sealed class IdempotencyRecord
{
    public Guid PlayerId { get; set; }
    public string Scope { get; set; } = "";
    public string Key { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string? ResponseJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class InboxRecord
{
    public Guid EventId { get; set; }
    public string PayloadHash { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class WalletDesignTimeFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql("Host=localhost;Database=wallet_db;Username=wallet_app;Password=local-wallet").Options);
}
