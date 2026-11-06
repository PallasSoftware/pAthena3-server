using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Ro3Emu.Server;
using Ro3Emu.Server.Accounts;
using Ro3Emu.Server.Accounts.Persistence;

namespace Ro3Emu.Tests;

public sealed class EfTransactionOwnershipTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ro3-ef-ownership-" + Guid.NewGuid().ToString("N"));

    public static IEnumerable<object[]> Attempts => new[]
    {
        "commit", "rollback", "tx-commit", "tx-rollback", "save", "rollback-save", "release-save",
        "commit-async", "rollback-async", "save-async", "rollback-save-async", "release-save-async",
        "native-commit", "native-rollback", "native-save", "native-rollback-save", "native-release-save", "native-dispose",
        "native-commit-async", "detach-begin", "dispose-begin", "raw-commit", "raw-end", "raw-rollback",
        "raw-save", "raw-rollback-save", "raw-release-save", "raw-begin", "prepared-commit"
    }.SelectMany<string, object[]>(mode => [[mode, false], [mode, true]]);

    private sealed class Attack(string mode, bool swallow) : DbPlan
    {
        internal override int Bytes => 128;
        internal override string Label => "test/ef-transaction-ownership";
        internal override string OperationId => "ownership:" + mode + ":" + swallow;
        internal override IReadOnlyList<OwnerRevisionKey> EntityWrites => [RevisionStore.Entity("server_state", "hardening")];
        internal override object Execute(DbPlanExecution execution)
        {
            var context = execution.Game;
            RealmStatePersistence.Write(context, "hardening", "tentative");
            var native = execution.Transaction;
            Exception? denied = null;
            try { Attempt(context, mode); }
            catch (Exception error) { denied = error; }
            Assert.True(denied is InvalidOperationException || denied is SqliteException { SqliteErrorCode: 23 },
                "Transaction control was not refused safely: " + mode + ": " + denied);
            if (!swallow || native.Connection is null) throw new IOException("Synthetic abort after denied transaction control.", denied);
            // Detaching/disposal may clear EF's wrapper, but must leave the native transaction intact.
            context.Database.UseTransaction(native);
            return RealmStatePersistence.Write(context, "hardening", "committed");
        }
    }

    private static void Attempt(GameDbContext context, string mode)
    {
        var tx = context.Database.CurrentTransaction ?? throw new InvalidOperationException("Missing enlisted transaction.");
        var native = tx.GetDbTransaction();
        switch (mode)
        {
            case "commit": context.Database.CommitTransaction(); break;
            case "rollback": context.Database.RollbackTransaction(); break;
            case "tx-commit": tx.Commit(); break;
            case "tx-rollback": tx.Rollback(); break;
            case "save": tx.CreateSavepoint("g1"); break;
            case "rollback-save": tx.RollbackToSavepoint("g1"); break;
            case "release-save": tx.ReleaseSavepoint("g1"); break;
            case "commit-async": context.Database.CommitTransactionAsync().GetAwaiter().GetResult(); break;
            case "rollback-async": context.Database.RollbackTransactionAsync().GetAwaiter().GetResult(); break;
            case "save-async": tx.CreateSavepointAsync("g1").GetAwaiter().GetResult(); break;
            case "rollback-save-async": tx.RollbackToSavepointAsync("g1").GetAwaiter().GetResult(); break;
            case "release-save-async": tx.ReleaseSavepointAsync("g1").GetAwaiter().GetResult(); break;
            case "native-commit": native.Commit(); break;
            case "native-rollback": native.Rollback(); break;
            case "native-save": native.Save("g1"); break;
            case "native-rollback-save": native.Rollback("g1"); break;
            case "native-release-save": native.Release("g1"); break;
            case "native-dispose": native.Dispose(); break;
            case "native-commit-async": native.CommitAsync().GetAwaiter().GetResult(); break;
            case "detach-begin": context.Database.UseTransaction((System.Data.Common.DbTransaction?)null); context.Database.BeginTransaction(); break;
            case "dispose-begin": tx.Dispose(); context.Database.BeginTransaction(); break;
            case "prepared-commit":
                using (var command = context.Database.GetDbConnection().CreateCommand())
                {
                    command.Transaction = native;
                    command.CommandText = "COMMIT";
                    command.Prepare();
                    command.ExecuteNonQuery();
                }
                break;
            default:
                context.Database.ExecuteSqlRaw(mode switch
                {
                    "raw-commit" => "/* synthetic */ CoMmIt",
                    "raw-end" => "END TRANSACTION",
                    "raw-rollback" => "ROLLBACK",
                    "raw-save" => "SAVEPOINT hostile",
                    "raw-rollback-save" => "ROLLBACK TO g1",
                    "raw-release-save" => "RELEASE g1",
                    "raw-begin" => "BEGIN",
                    _ => throw new InvalidOperationException("Unknown test attack.")
                });
                break;
        }
    }

    private sealed class Receipt(string id) : DbReadPlan
    {
        internal override int Bytes => 1;
        internal override string Label => "test/ownership-receipt";
        internal override object Execute(SqliteConnection db, SqliteTransaction tx) => OperationReceipts.TryRead(db, tx, id, out _);
    }

    [Theory]
    [MemberData(nameof(Attempts))]
    public async Task OnlyTheWriterCanCompleteOrReshapeTheTransaction(string mode, bool swallow)
    {
        using var store = new AccountStore(Options.Create(new ServerOptions { DataDir = _directory }));
        var plan = new Attack(mode, swallow);
        bool committed = swallow && mode is not ("native-rollback" or "native-dispose");
        if (committed) Assert.Equal(1, await store.WriteLegacyAsync<int>(plan));
        else await Assert.ThrowsAsync<IOException>(() => store.WriteLegacyAsync<int>(plan));
        Assert.Equal(committed ? "committed" : null, await store.ReadAsync<string?>(new SeasonStateReadPlan("hardening")));
        var revision = store.ReadSync<OwnerRevisionValue>(new OwnerRevisionReadPlan(RevisionStore.Entity("server_state", "hardening")));
        Assert.Equal(committed, revision.Exists);
        Assert.Equal(committed ? 1 : 0, revision.Revision);
        Assert.Equal(committed, await store.ReadAsync<bool>(new Receipt(plan.OperationId)));
        Assert.Equal(1, await store.WriteLegacyAsync<int>(new SeasonStateWritePlan("after", "healthy")));
    }

    private sealed class ReadRollback : DbReadPlan
    {
        internal override int Bytes => 1;
        internal override string Label => "test/read-native-rollback";
        internal override object Execute(DbPlanExecution execution)
        {
            execution.Game.Database.CurrentTransaction?.GetDbTransaction().Rollback();
            return 0;
        }
    }

    [Fact]
    public async Task AReaderAlsoRecoversAfterARejectedNativeRollback()
    {
        using var store = new AccountStore(Options.Create(new ServerOptions { DataDir = _directory }));
        for (int i = 0; i < 4; i++)
        {
            var error = await Assert.ThrowsAsync<SqliteException>(() => store.ReadAsync<int>(new ReadRollback()));
            Assert.Equal(23, error.SqliteErrorCode);
            Assert.Null(await store.ReadAsync<string?>(new SeasonStateReadPlan("hardening")));
        }
    }

    private sealed class Blocker(ManualResetEventSlim entered, ManualResetEventSlim release, TimeSpan budget) : DbPlan
    {
        internal override int Bytes => 1;
        internal override string Label => "test/ownership-blocker";
        internal override object Execute(SqliteConnection db, SqliteTransaction tx)
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic group barrier.");
            Thread.Sleep(budget + TimeSpan.FromMilliseconds(50)); // force this group's time cap before the backlog
            return 0;
        }
    }

    [Fact]
    public async Task PoisonedNativeHandleRollsBackEarlierJobsAndReceiptsInTheSameGroup()
    {
        using (var setup = new AccountStore(Options.Create(new ServerOptions { DataDir = _directory }))) { }
        string file = Path.Combine(_directory, "ro3emu.db");
        var factory = new SqliteGameContextFactory();
        var budget = TimeSpan.FromMilliseconds(500);
        using var writer = new DbWriter("Data Source=" + file, new DbAccessMetrics(), contexts: factory) { MaxGroupExecution = budget };
        using var readers = new DbReadPool(file, new DbAccessMetrics(), contexts: factory);
        DbPlan State(string key)
        {
            var plan = new RoleItemPlan([new SeasonStateWritePlan(key, "committed")], "test/group-state", operationId: key);
            plan.ExpectEntities([new(RevisionStore.Entity("server_state", key), 0, false)]);
            return plan;
        }
        await writer.ExecuteAsync<object?[]>(State("warmup"));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var blocked = writer.ExecuteAsync<int>(new Blocker(entered, release, budget));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var before = writer.ExecuteAsync<object?[]>(State("before"));
        var attack = new Attack("native-rollback", false);
        attack.ExpectEntities([new(RevisionStore.Entity("server_state", "hardening"), 0, false)]);
        var poisoned = writer.ExecuteAsync<int>(attack);
        var after = writer.ExecuteAsync<object?[]>(State("after"));
        release.Set();
        await blocked;
        await Assert.ThrowsAnyAsync<Exception>(() => before);
        await Assert.ThrowsAsync<IOException>(() => poisoned);
        Assert.Equal(1, (await after)[0]);
        Assert.Null(await readers.ExecuteAsync<string?>(new SeasonStateReadPlan("before")));
        Assert.Null(await readers.ExecuteAsync<string?>(new SeasonStateReadPlan("hardening")));
        Assert.Equal("committed", await readers.ExecuteAsync<string?>(new SeasonStateReadPlan("after")));
        Assert.False(await readers.ExecuteAsync<bool>(new Receipt("before")));
        Assert.False(await readers.ExecuteAsync<bool>(new Receipt(attack.OperationId)));
        Assert.True(await readers.ExecuteAsync<bool>(new Receipt("after")));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
