using System.Security.Claims;
using System.Text.Json;
using MiniWallet.Users.Application.Abstractions;
namespace MiniWallet.Users.Api.Auth;

// Replace this provider-specific adapter when changing claim conventions; business use cases stay unchanged.
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
                    if (role.GetString() == "player") permissions.UnionWith([Permissions.Create, Permissions.ManageOwn]);
                    if (role.GetString() == "admin") permissions.Add(Permissions.ManageAny);
                }
        }
        return new Actor(principal.FindFirstValue("iss")!, principal.FindFirstValue("sub")!,
            principal.FindFirstValue("email_verified") == "true", permissions);
    }
}
