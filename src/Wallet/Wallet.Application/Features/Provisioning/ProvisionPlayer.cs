using System.Security.Cryptography;
using System.Text.Json;
using MiniWallet.Contracts;
using MiniWallet.Wallet.Application.Abstractions;
namespace MiniWallet.Wallet.Application.Features.Provisioning;

public interface IProvisioningStore
{
    Task<bool> RecordEventAsync(Guid eventId, string payloadHash, CancellationToken cancellationToken);
    Task AssociateAsync(Guid playerId, string issuer, string subject, CancellationToken cancellationToken);
    Task CreateWalletsAsync(Guid playerId, CancellationToken cancellationToken);
}
public sealed class InvalidProvisioningMessage(string reason) : Exception(reason);
public sealed class ProvisionPlayer(IProvisioningStore store, IUnitOfWork unitOfWork)
{
    public async Task ExecuteAsync(PlayerCreatedV1 message, CancellationToken cancellationToken = default)
    {
        if (message.EventId == Guid.Empty || message.PlayerId == Guid.Empty || message.OccurredAt == default
            || string.IsNullOrWhiteSpace(message.Subject) || message.Subject.Length > 200
            || string.IsNullOrWhiteSpace(message.Issuer) || message.Issuer.Length > 500
            || !Uri.TryCreate(message.Issuer, UriKind.Absolute, out var issuer) || issuer.Scheme is not ("http" or "https"))
            throw new InvalidProvisioningMessage("Invalid player-created fields.");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(message)));
        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            if (await store.RecordEventAsync(message.EventId, hash, cancellationToken))
            {
                await store.AssociateAsync(message.PlayerId, message.Issuer, message.Subject, cancellationToken);
                await store.CreateWalletsAsync(message.PlayerId, cancellationToken);
            }
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
