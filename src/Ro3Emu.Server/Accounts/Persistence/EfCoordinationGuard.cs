using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ro3Emu.Server.Accounts.Persistence;

/// <summary>Tag EF command execution for the native authorizer. This covers raw SQL and ExecuteUpdate/Delete,
/// which bypass SaveChanges. No SQL text parsing and no change to coordinator/legacy permissions.</summary>
internal sealed class EfCoordinationGuard : DbCommandInterceptor
{
    private static void Begin(DbCommand command, CommandEventData eventData)
    {
        bool tracked = eventData.CommandSource == CommandSource.SaveChanges;
        if (tracked && eventData.Context is GameDbContext context) context.ValidateWrites();
        DbWriteGuard.EfCommand(command.Connection, true, tracked);
    }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    { Begin(command, eventData); return result; }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result)
    { Begin(command, eventData); return result; }
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result)
    { Begin(command, eventData); return result; }
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        DbWriteGuard.EfCommand(command.Connection, false);
        return eventData.Context is GameDbContext { BorrowsWorkerTransaction: true }
            ? new EfGuardedReader(result, command.Connection, eventData.CommandSource == CommandSource.SaveChanges)
            : result;
    }
    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    { DbWriteGuard.EfCommand(command.Connection, false); return result; }
    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    { DbWriteGuard.EfCommand(command.Connection, false); return result; }
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => DbWriteGuard.EfCommand(command.Connection, false);
    public override void CommandCanceled(DbCommand command, CommandEndEventData eventData) => DbWriteGuard.EfCommand(command.Connection, false);

    // Runtime contexts are thread-bound. Refuse async EF commands on a native guarded handle rather than
    // carrying native permissions over an asynchronous continuation on another thread.
    private static void AsyncCommand(DbCommand command) => DbWriteGuard.RefuseAsyncEfCommand(command.Connection);
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { AsyncCommand(command); return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { AsyncCommand(command); return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    { AsyncCommand(command); return ValueTask.FromResult(result); }
}
