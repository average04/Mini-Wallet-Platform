using System.Reflection;
namespace MiniWallet.Wallet.Tests.Architecture;

public sealed class DependencyTests
{
    [Theory]
    [InlineData("MiniWallet.Wallet.Domain.AssemblyMarker, Wallet.Domain")]
    [InlineData("MiniWallet.Wallet.Application.AssemblyMarker, Wallet.Application")]
    public void Inner_layers_do_not_depend_on_hosts_or_persistence(string marker)
    {
        var references = Type.GetType(marker, throwOnError: true)!.Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, r => r.Name!.Contains("Infrastructure")
            || r.Name.EndsWith(".Api") || r.Name.EndsWith(".Worker")
            || r.Name.StartsWith("Microsoft.EntityFrameworkCore")
            || r.Name.StartsWith("Npgsql") || r.Name.StartsWith("RabbitMQ"));
        Assert.DoesNotContain(references, r => r.Name!.StartsWith("Users."));
    }
}

