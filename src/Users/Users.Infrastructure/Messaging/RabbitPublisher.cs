using System.Text;
using MiniWallet.Users.Application.Features.Outbox;
using RabbitMQ.Client;
namespace MiniWallet.Users.Infrastructure.Messaging;

public sealed class RabbitPublisher(string connectionString) : IEventPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;
    private bool broken;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { await EnsureConnected(cancellationToken); }
        finally { gate.Release(); }
    }
    private async Task EnsureConnected(CancellationToken ct)
    {
        if (!broken && connection is { IsOpen: true } && channel is { IsOpen: true }) return;
        await CloseAsync();
        connection = await RabbitTopology.Factory(connectionString).CreateConnectionAsync(ct);
        channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), ct);
        await RabbitTopology.DeclareAsync(channel, ct);
        broken = false;
    }
    public async Task PublishAsync(OutgoingEvent message, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (broken || channel is not { IsOpen: true })
                throw new InvalidOperationException("Publisher must be connected before claiming an outbox row.");
            // Confirmation tracking throws PublishException for both nack and basic.return,
            // including the normal unroutable sequence of basic.return followed by basic.ack.
            await channel!.BasicPublishAsync(RabbitTopology.Exchange, message.Type, mandatory: true,
                new BasicProperties { Persistent = true, ContentType = "application/json", Type = message.Type, MessageId = message.EventId.ToString() },
                Encoding.UTF8.GetBytes(message.PayloadJson), cancellationToken);
        }
        catch { broken = true; throw; }
        finally { gate.Release(); }
    }
    private async Task CloseAsync()
    {
        if (channel is not null) { await channel.DisposeAsync(); channel = null; }
        if (connection is not null) { await connection.DisposeAsync(); connection = null; }
    }
    public async ValueTask DisposeAsync() { await CloseAsync(); gate.Dispose(); }
}
