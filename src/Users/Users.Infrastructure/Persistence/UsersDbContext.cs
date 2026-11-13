using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MiniWallet.Users.Domain;

namespace MiniWallet.Users.Infrastructure.Persistence;

public sealed class UsersDbContext(DbContextOptions<UsersDbContext> options) : DbContext(options)
{
    public DbSet<PlayerProfile> Profiles => Set<PlayerProfile>();
    public DbSet<IdentityAssociation> Identities => Set<IdentityAssociation>();
    public DbSet<OutboxEvent> Outbox => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<PlayerProfile>(b =>
        {
            b.ToTable("profiles", t => t.HasCheckConstraint("ck_profile_name", "length(btrim(display_name)) BETWEEN 1 AND 100"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.DisplayName).HasMaxLength(100);
        });
        model.Entity<IdentityAssociation>(b =>
        {
            b.ToTable("player_identities");
            b.HasKey(x => new { x.Issuer, x.Subject });
            b.Property(x => x.Issuer).HasMaxLength(500);
            b.Property(x => x.Subject).HasMaxLength(200);
            b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OutboxEvent>(b =>
        {
            b.ToTable("outbox");
            b.HasKey(x => x.EventId);
            b.Property(x => x.EventId).ValueGeneratedNever();
            b.Property(x => x.Type).HasMaxLength(100);
            b.Property(x => x.PayloadJson).HasColumnType("jsonb");
            b.HasIndex(x => new { x.OccurredAt, x.EventId }).HasFilter("published_at IS NULL");
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
public sealed class OutboxEvent
{
    public Guid EventId { get; set; }
    public string Type { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
public sealed class UsersDesignTimeFactory : IDesignTimeDbContextFactory<UsersDbContext>
{
    public UsersDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<UsersDbContext>()
        .UseNpgsql("Host=localhost;Database=users_db;Username=users_app;Password=local-users").Options);
}
