using MiniWallet.Wallet.Infrastructure;
using Npgsql;
namespace MiniWallet.Wallet.Tests.Unit;
public sealed class PersistenceErrorTests
{
    [Fact]
    public void Wrapped_provider_connection_failure_is_storage_unavailable()
        => Assert.True(PersistenceErrors.IsFailure(new InvalidOperationException("Transient provider failure", new NpgsqlException("Connection lost"))));
    [Fact]
    public void Programming_errors_are_not_misreported_as_database_outages()
        => Assert.False(PersistenceErrors.IsFailure(new InvalidOperationException("Invalid application state")));
}
