using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ro3Emu.Server.Accounts.Persistence;

/// <summary>The worker owns transactions and savepoints. EF transaction APIs must fail before changing
/// their wrapper or the native transaction. Unregistered offline/kernel connections remain independent.</summary>
internal sealed class EfTransactionGuard : DbTransactionInterceptor
{
    public override InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection,
        TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
    { DbWriteGuard.AssertTransactionControlAllowed(connection); return result; }

    public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData,
        InterceptionResult result) => Check(transaction, result);
    public override InterceptionResult TransactionRollingBack(DbTransaction transaction, TransactionEventData eventData,
        InterceptionResult result) => Check(transaction, result);
    public override InterceptionResult CreatingSavepoint(DbTransaction transaction, TransactionEventData eventData,
        InterceptionResult result) => Check(transaction, result);
    public override InterceptionResult RollingBackToSavepoint(DbTransaction transaction, TransactionEventData eventData,
        InterceptionResult result) => Check(transaction, result);
    public override InterceptionResult ReleasingSavepoint(DbTransaction transaction, TransactionEventData eventData,
        InterceptionResult result) => Check(transaction, result);

    private static InterceptionResult Check(DbTransaction transaction, InterceptionResult result)
    { DbWriteGuard.AssertTransactionControlAllowed(transaction.Connection); return result; }

    public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
        TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(TransactionStarting(connection, eventData, result));
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
        TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Check(transaction, result));
    public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction,
        TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Check(transaction, result));
    public override ValueTask<InterceptionResult> CreatingSavepointAsync(DbTransaction transaction,
        TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Check(transaction, result));
    public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(DbTransaction transaction,
        TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Check(transaction, result));
    public override ValueTask<InterceptionResult> ReleasingSavepointAsync(DbTransaction transaction,
        TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Check(transaction, result));
}
