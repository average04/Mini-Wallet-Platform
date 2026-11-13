using System.Text.Json;
using Microsoft.Extensions.Logging;
using MiniWallet.Contracts;
using MiniWallet.Wallet.Application.Features.Provisioning;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
namespace MiniWallet.Wallet.Infrastructure.Messaging;

public sealed class PlayerEventConsumer(string connectionString, ILogger<PlayerEventConsumer> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    public async Task RunAsync(Func<PlayerCreatedV1, CancellationToken, Task> handle, CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunConnection(handle, cancellationToken);
                failures = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                logger.LogWarning("Provisioning connection unavailable ({ErrorType}); reconnecting.", error.GetType().Name);
            }
            await Task.Delay(Backoff(failures++), cancellationToken);
        }
    }
    private async Task RunConnection(Func<PlayerCreatedV1, CancellationToken, Task> handle, CancellationToken stoppingToken)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = await RabbitTopology.Factory(connectionString).CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        connection.ConnectionShutdownAsync += (_, _) => { disconnected.TrySetResult(); return Task.CompletedTask; };
        channel.ChannelShutdownAsync += (_, _) => { disconnected.TrySetResult(); return Task.CompletedTask; };
        channel.CallbackExceptionAsync += (_, _) => { disconnected.TrySetResult(); return Task.CompletedTask; };
        await RabbitTopology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, 1, global: false, stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.UnregisteredAsync += (_, _) => { disconnected.TrySetResult(); return Task.CompletedTask; };
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            // RabbitMQ owns the delivery buffer; copy before awaiting.
            var body = delivery.Body.ToArray();
            var ct = session.Token;
            PlayerCreatedV1 message;
            try
            {
                message = JsonSerializer.Deserialize<PlayerCreatedV1>(body, Json)
                    ?? throw new InvalidProvisioningMessage("Missing payload.");
                if (delivery.BasicProperties.Type != RabbitTopology.RoutingKey
                    || !Guid.TryParse(delivery.BasicProperties.MessageId, out var id) || id != message.EventId)
                    throw new InvalidProvisioningMessage("Invalid envelope.");
            }
            catch (Exception error) when (error is JsonException or InvalidProvisioningMessage)
            {
                logger.LogWarning("Retaining malformed provisioning message in {DeadQueue}.", RabbitTopology.DeadQueue);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
                return;
            }
            var attempts = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await handle(message, ct);
                    await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
                    return;
                }
                catch (InvalidProvisioningMessage)
                {
                    logger.LogWarning("Retaining conflicting provisioning event {EventId} in {DeadQueue}.", message.EventId, RabbitTopology.DeadQueue);
                    await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception error)
                {
                    logger.LogWarning("Provisioning event {EventId} failed ({ErrorType}); retaining unacknowledged delivery.", message.EventId, error.GetType().Name);
                }
                try { await Task.Delay(Backoff(attempts++), ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            }
        };
        await channel.BasicConsumeAsync(RabbitTopology.Queue, autoAck: false, consumer, stoppingToken);
        try { await disconnected.Task.WaitAsync(stoppingToken); }
        finally { await session.CancelAsync(); }
        // Disposing the channel requeues its unacknowledged delivery; the inbox handles redelivery.
    }
    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
}
