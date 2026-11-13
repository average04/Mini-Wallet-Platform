using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MiniWallet.Users.Infrastructure;

// The HTTP adapter need not know which database library the persistence adapter uses.
public static class PersistenceErrors
{
    public static bool IsFailure(Exception error) => error is DbUpdateException or NpgsqlException or TimeoutException;
}
