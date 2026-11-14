using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MiniWallet.Wallet.Infrastructure;

// The HTTP adapter need not know which database library the persistence adapter uses.
public static class PersistenceErrors
{
    public static bool IsFailure(Exception error) => error is DbUpdateException or NpgsqlException or TimeoutException
        // Npgsql's EF execution strategy wraps connection failures when retries are disabled.
        || error is InvalidOperationException { InnerException: NpgsqlException };
}
