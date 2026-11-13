using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniWallet.Wallet.Application.Abstractions;
using MiniWallet.Wallet.Infrastructure.Persistence;

namespace MiniWallet.Wallet.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWalletInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<WalletDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("Database")
                ?? throw new InvalidOperationException("ConnectionStrings:Database is required."),
                postgres => postgres.CommandTimeout(10)));
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }
}
