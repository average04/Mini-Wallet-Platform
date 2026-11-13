using System.Reflection;
namespace MiniWallet.Users.Tests.Architecture;

public sealed class DependencyTests
{
    [Theory]
    [InlineData("MiniWallet.Users.Domain.AssemblyMarker, Users.Domain")]
    [InlineData("MiniWallet.Users.Application.AssemblyMarker, Users.Application")]
    public void Inner_layers_do_not_depend_on_hosts_or_persistence(string marker)
    {
        var references = Type.GetType(marker, throwOnError: true)!.Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, r => r.Name!.Contains("Infrastructure")
            || r.Name.EndsWith(".Api") || r.Name.EndsWith(".Worker")
            || r.Name.StartsWith("Microsoft.EntityFrameworkCore")
            || r.Name.StartsWith("Npgsql") || r.Name.StartsWith("RabbitMQ"));
        Assert.DoesNotContain(references, r => r.Name!.StartsWith("Wallet."));
    }
}

