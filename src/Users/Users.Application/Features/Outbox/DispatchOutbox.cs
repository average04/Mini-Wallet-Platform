using MiniWallet.Users.Application.Abstractions;
namespace MiniWallet.Users.Application.Features.Outbox;
public sealed record OutgoingEvent(Guid EventId, string Type, string PayloadJson);
public interface IOutboxQueue
{
    Task<OutgoingEvent?> ClaimNextAsync(CancellationToken cancellationToken);
    Task MarkPublishedAsync(Guid eventId, CancellationToken cancellationToken);
}
public interface IEventPublisher
{
    Task PublishAsync(OutgoingEvent message, CancellationToken cancellationToken);
}
public sealed class DispatchOutbox(IOutboxQueue queue, IEventPublisher publisher, IUnitOfWork unitOfWork)
{
    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // One bounded transaction; all idle/backoff delays belong outside it.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        var ct = deadline.Token;
        await unitOfWork.BeginAsync(ct);
        try
        {
            var message = await queue.ClaimNextAsync(ct);
            if (message is not null)
            {
                await publisher.PublishAsync(message, ct);
                await queue.MarkPublishedAsync(message.EventId, ct);
            }
            await unitOfWork.CommitAsync(ct);
            return message is not null;
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
