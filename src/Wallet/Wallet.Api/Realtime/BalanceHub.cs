using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MiniWallet.Wallet.Api.Auth;
using MiniWallet.Wallet.Application;
using MiniWallet.Wallet.Application.Features.Reads;

namespace MiniWallet.Wallet.Api.Realtime;

[Authorize]
public sealed class BalanceHub(WalletQueries wallets) : Hub
{
    public const string Path = "/hubs/balances";
    internal static string PlayerGroup(Guid playerId) => "balance:" + playerId.ToString("N");

    public override async Task OnConnectedAsync()
    {
        try
        {
            // The caller never chooses a player/group. Provider-specific claims stay at this boundary.
            var playerId = await wallets.ResolvePlayerAsync(ActorMapper.Map(Context.User!), null, Context.ConnectionAborted);
            await Groups.AddToGroupAsync(Context.ConnectionId, PlayerGroup(playerId), Context.ConnectionAborted);
            // A client must wait for enrollment before fetching its initial snapshot.
            await Clients.Caller.SendAsync("Ready", Context.ConnectionAborted);
            await base.OnConnectedAsync();
        }
        catch (RequestFailure failure)
        {
            Context.Abort();
            throw new HubException(failure.Code);
        }
    }
}
