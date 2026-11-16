using StackExchange.Redis;

namespace MiniWallet.Wallet.Api.Realtime;

public static class RealtimeSetup
{
    public static IServiceCollection AddBalanceUpdates(this IServiceCollection services, IConfiguration configuration)
    {
        var signalR = services.AddSignalR();
        // Omitting Redis supports a single local API process. Compose always configures the backplane.
        if (configuration.GetConnectionString("Redis") is { Length: > 0 } redis)
        {
            signalR.AddStackExchangeRedis(redis, options =>
            {
                options.Configuration.ChannelPrefix = RedisChannel.Literal("mini-wallet:balances:v1");
                options.Configuration.AbortOnConnectFail = false;
                options.Configuration.ConnectTimeout = 1000;
                options.Configuration.AsyncTimeout = 1000;
                options.Configuration.BacklogPolicy = BacklogPolicy.FailFast;
            });
        }
        services.AddSingleton<BalanceUpdateNotifier>();
        return services;
    }
}
