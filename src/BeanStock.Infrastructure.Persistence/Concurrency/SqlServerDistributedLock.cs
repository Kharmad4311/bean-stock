using System.Data;
using System.Data.Common;
using System.Globalization;
using BeanStock.Application.Abstractions.Concurrency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BeanStock.Infrastructure.Persistence.Concurrency;

/// <summary>
/// Distributed lock based on SQL Server application locks (<c>sp_getapplock</c>): no extra infrastructure,
/// shared by every instance connected to the database.
/// </summary>
/// <remarks>
/// <para>
/// The lock is owned by the session of the connection of the scoped <see cref="BeanStockDbContext"/>, which
/// stays open until the lock is released. The caller's transaction uses this same connection.
/// </para>
/// <para>
/// If the connection is lost, SQL Server releases the lock with the session, and EF Core may later reopen the
/// connection on a new session that does not hold it. <see cref="IDistributedLockHandle.IsStillHeldAsync"/>
/// detects it: it asks SQL Server, inside the caller's transaction, whether the current session still owns
/// the lock (<c>APPLOCK_MODE</c>). Because lock and transaction share the session, a positive answer followed
/// by a commit is safe: if the session dies in between, the commit fails too.
/// </para>
/// <para>
/// A crashed process releases the lock as soon as SQL Server closes its session: no lock can stay stuck.
/// </para>
/// </remarks>
internal sealed class SqlServerDistributedLock(BeanStockDbContext dbContext) : IDistributedLock
{
    // sp_getapplock returns 0 or 1 when granted, -1 on timeout (the lock is held elsewhere).
    private const int LockTimeoutResult = -1;

    private const string AcquireSql = """
        DECLARE @result int;
        EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
        SELECT @result;
        """;

    private const string CheckSql = """
        SELECT APPLOCK_MODE('public', @resource, 'Session');
        """;

    private const string ReleaseSql = """
        EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';
        """;

    public async Task<IDistributedLockHandle?> TryAcquireAsync(string resource, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        // Opened manually: EF Core keeps the connection open until CloseConnectionAsync.
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        int result;
        try
        {
            await using var command = CreateCommand(resource);
            command.CommandText = AcquireSql;
            result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }
        catch
        {
            await dbContext.Database.CloseConnectionAsync();
            throw;
        }

        if (result >= 0)
        {
            return new LockHandle(this, resource);
        }

        await dbContext.Database.CloseConnectionAsync();

        return result == LockTimeoutResult
            ? null
            : throw new InvalidOperationException(
                $"sp_getapplock failed for resource '{resource}' with code {result}.");
    }

    private DbCommand CreateCommand(string resource)
    {
        var command = dbContext.Database.GetDbConnection().CreateCommand();

        // Joins the caller's transaction, if any: SqlClient refuses a command outside the pending transaction.
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.Value = resource;
        command.Parameters.Add(parameter);
        return command;
    }

    private async Task<bool> IsHeldAsync(string resource, CancellationToken cancellationToken)
    {
        // A connection closed after a failure has lost its session, and the lock with it.
        if (!IsConnectionOpen())
        {
            return false;
        }

        try
        {
            await using var command = CreateCommand(resource);
            command.CommandText = CheckSql;
            var mode = await command.ExecuteScalarAsync(cancellationToken) as string;
            return string.Equals(mode, "Exclusive", StringComparison.Ordinal);
        }
        catch (DbException)
        {
            // Connection lost: the session, and the lock with it, are gone.
            return false;
        }
    }

    private async Task ReleaseAsync(string resource)
    {
        try
        {
            // Connection already closed after a failure: the lock went away with the session.
            if (!IsConnectionOpen())
            {
                return;
            }

            await using var command = CreateCommand(resource);
            command.CommandText = ReleaseSql;

            // Not cancelable: the lock must be released even when the caller has given up.
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (DbException)
        {
            // The connection is broken: SQL Server has already released the lock with the session.
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private bool IsConnectionOpen() => dbContext.Database.GetDbConnection().State == ConnectionState.Open;

    private sealed class LockHandle(SqlServerDistributedLock owner, string resource) : IDistributedLockHandle
    {
        private bool _released;

        public Task<bool> IsStillHeldAsync(CancellationToken cancellationToken) =>
            _released ? Task.FromResult(false) : owner.IsHeldAsync(resource, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            await owner.ReleaseAsync(resource);
        }
    }
}
