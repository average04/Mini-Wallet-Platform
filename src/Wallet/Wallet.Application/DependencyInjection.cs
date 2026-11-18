using Microsoft.Extensions.DependencyInjection;
using MiniWallet.Wallet.Application.Features.Transactions;

namespace MiniWallet.Wallet.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddWalletApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<AssemblyMarker>());
        services.AddScoped<TransactionRequestValidator>();
        services.AddScoped<ITransactionHandler, BetHandler>();
        services.AddScoped<ITransactionHandler, WinHandler>();
        services.AddScoped<ITransactionHandler, BonusHandler>();
        return services;
    }
}
