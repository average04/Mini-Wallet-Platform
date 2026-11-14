using System.Security.Claims;
using System.Text.Json;
using MiniWallet.Wallet.Application.Abstractions;
namespace MiniWallet.Wallet.Api.Auth;

// Provider-specific claims stop here. Application only sees platform permissions.
public static class ActorMapper
{
    public static Actor Map(ClaimsPrincipal principal)
    {
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        var raw = principal.FindFirstValue("realm_access");
        if (raw is not null)
        {
            using var roles = JsonDocument.Parse(raw);
            if (roles.RootElement.TryGetProperty("roles", out var values) && values.ValueKind == JsonValueKind.Array)
                foreach (var role in values.EnumerateArray())
                {
                    if (role.GetString() == "player") permissions.Add(Permissions.PlaceBet);
                    if (role.GetString() == "admin")
                        permissions.UnionWith([Permissions.ReadAny, Permissions.RecordWin, Permissions.GrantBonus]);
                }
        }
        return new Actor(principal.FindFirstValue("iss")!, principal.FindFirstValue("sub")!,
            principal.FindFirstValue("email_verified") == "true", permissions);
    }
}
