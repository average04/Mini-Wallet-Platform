using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
namespace MiniWallet.Users.Infrastructure.Messaging;

// Protocol topology is deliberately owned by each service; neither needs the other process to start.
public static class RabbitTopology
{
    public const string Exchange = "mini-wallet.events";
    public const string RoutingKey = "player.created.v1";
    public const string Queue = "mini-wallet.provisioning";
    public const string DeadExchange = "mini-wallet.dead";
    public const string DeadQueue = "mini-wallet.provisioning.dead";
    public static string ConnectionString(IConfiguration configuration)
    {
        if (configuration["Broker:Uri"] is { } uri) return uri;
        var host = configuration["Broker:Host"] ?? "localhost";
        var port = configuration["Broker:Port"] ?? "5672";
        var user = Uri.EscapeDataString(configuration["Broker:Username"] ?? "wallet");
        var password = Uri.EscapeDataString(configuration["Broker:Password"] ?? throw new InvalidOperationException("Broker:Password is required."));
        return $"amqp://{user}:{password}@{host}:{port}/";
    }
    public static ConnectionFactory Factory(string connectionString) => new()
    {
        Uri = new Uri(connectionString), AutomaticRecoveryEnabled = false,
        RequestedConnectionTimeout = TimeSpan.FromSeconds(3), ContinuationTimeout = TimeSpan.FromSeconds(3),
        RequestedHeartbeat = TimeSpan.FromSeconds(10), ConsumerDispatchConcurrency = 1
    };
    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadExchange, ExchangeType.Direct, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(DeadQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(DeadQueue, DeadExchange, RoutingKey, cancellationToken: ct);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadExchange, ["x-dead-letter-routing-key"] = RoutingKey }, cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, RoutingKey, cancellationToken: ct);
    }
}
