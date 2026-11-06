using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ro3Emu.Server.Accounts.Persistence;

/// <summary>One worker job and its nested children share one lazily enlisted EF context.
/// Disposal at the job boundary discards tracking before the writer rolls back its savepoint.</summary>
internal sealed class DbPlanExecution(SqliteConnection connection, SqliteTransaction transaction,
    IDbContextFactory<GameDbContext>? factory) : IDisposable
{
    internal SqliteConnection Connection { get; } = connection;
    internal SqliteTransaction Transaction { get; } = transaction;
    private GameDbContext? _game;
    internal GameDbContext Game => _game ??= GameDbContext.Borrow(Connection, Transaction,
        factory ?? throw new InvalidOperationException("An EF plan requires a registered GameDbContext factory."));

    internal object? Run(DbPlan plan) => NativeErrors(() => plan.Execute(this));
    internal object? Run(DbReadPlan plan) => NativeErrors(() => plan.Execute(this));

    private static object? NativeErrors(Func<object?> execute)
    {
        try { return execute(); }
        catch (DbUpdateException error) when (error.InnerException is SqliteException)
        {
            // Keep native failure classification at the SQLite worker edge, never inside EF gameplay plans.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    public void Dispose() => _game?.Dispose();
}

/// <summary>Explicit SQLite composition for the engine's existing direct-store API. Never a plan fallback.</summary>
internal sealed class SqliteGameContextFactory : IDbContextFactory<GameDbContext>
{
    public GameDbContext CreateDbContext() => new(new DbContextOptionsBuilder<GameDbContext>().UseSqlite().Options);
}
