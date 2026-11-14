using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniWallet.Users.Application.Abstractions;
using MiniWallet.Users.Infrastructure.Persistence;
namespace MiniWallet.Users.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddUsersInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<UsersDbContext>((provider, options) => options.UseNpgsql(
            provider.GetRequiredService<IConfiguration>().GetConnectionString("Database")
                ?? throw new InvalidOperationException("ConnectionStrings:Database is required."),
            postgres => postgres.CommandTimeout(10)));
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<IPlayerEventOutbox, PlayerEventOutbox>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<MiniWallet.Users.Application.Features.Outbox.IOutboxQueue, OutboxQueue>();
        return services;
    }
}
