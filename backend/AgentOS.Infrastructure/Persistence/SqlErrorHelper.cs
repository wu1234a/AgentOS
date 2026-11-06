using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Persistence;

/// <summary>Shared classification of SQL Server constraint violations.</summary>
public static class SqlErrorHelper
{
    // 2601: Cannot insert duplicate key row in object ... with unique index.
    // 2627: Violation of UNIQUE KEY constraint ...
    private const int DuplicateKeyRowErrorNumber = 2601;
    private const int UniqueConstraintErrorNumber = 2627;

    public static bool IsUniqueKeyViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException
                && sqlException.Number is DuplicateKeyRowErrorNumber or UniqueConstraintErrorNumber)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsUniqueKeyViolation(DbUpdateException exception) => IsUniqueKeyViolation((Exception)exception);
}
