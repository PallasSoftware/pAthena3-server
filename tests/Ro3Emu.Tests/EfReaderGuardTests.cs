using Microsoft.Data.Sqlite;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Ro3Emu.Server;
using Ro3Emu.Server.Accounts;
using Ro3Emu.Server.Accounts.Persistence;

namespace Ro3Emu.Tests;

public sealed class EfReaderGuardTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ro3-ef-reader-" + Guid.NewGuid().ToString("N"));
    public static IEnumerable<object[]> Attacks => new[]
    {
        "CREATE TABLE b1_escape(value TEXT)",
        "UPDATE owner_revision SET revision=revision+50 WHERE kind='server_state' AND id='victim'",
        "UPDATE owner_operation_receipt SET family='bypassed' WHERE operation_id='reader-seed'",
        "UPDATE server_state SET value='bypassed' WHERE key='victim'"
    }.SelectMany(sql => new[] { "from-sql-dispose", "next-result", "close", "dispose", "dispose-async" }
        .SelectMany<string, object[]>(mode => [[sql, mode, false], [sql, mode, true]]));

    private sealed class Seed : DbPlan
    {
        internal override int Bytes => 1;
        internal override string Label => "test/reader-seed";
        internal override string OperationId => "reader-seed";
        internal override IReadOnlyList<OwnerRevisionKey> EntityWrites => [RevisionStore.Entity("server_state", "victim")];
        internal override object Execute(DbPlanExecution execution) => RealmStatePersistence.Write(execution.Game, "victim", "before");
    }

    private sealed class Attack(string sql, string mode, bool swallow) : DbPlan
    {
        internal override int Bytes => 256;
        internal override string Label => "test/ef-reader-batch";
        internal override string OperationId => "reader-attack";
        internal override IReadOnlyList<OwnerRevisionKey> EntityWrites => [RevisionStore.Entity("server_state", "allowed")];
        internal override object Execute(DbPlanExecution execution)
        {
            var context = execution.Game;
            RealmStatePersistence.Write(context, "allowed", "tentative");
            var error = Record.Exception(() => Consume(context, "SELECT 'probe' AS key, 'read' AS value; " + sql, mode));
            Assert.Equal(23, Assert.IsType<SqliteException>(error).SqliteErrorCode);
            // The adapter must also surface errors that native SqliteDataReader.Close suppresses.
            if (!swallow) throw new IOException("Synthetic abort after guarded reader cleanup.", error);
            // A refused late statement must not leave EF authority active during coordinator work.
            Assert.Equal("before", RealmStatePersistence.Read(context, "victim"));
            return RealmStatePersistence.Write(context, "allowed", "committed");
        }
    }

    private static void Consume(GameDbContext context, string sql, string mode)
    {
        if (mode == "from-sql-dispose")
        {
            using var rows = context.RealmState.FromSqlRaw(sql).AsNoTracking().AsEnumerable().GetEnumerator();
            Assert.True(rows.MoveNext());
            Assert.Equal("read", rows.Current.Value);
            return; // Dispose without exhausting the first result set.
        }
        // Execute through EF's actual relational pipeline on the worker's enlisted context, not a raw connection.
        var command = context.GetService<IRawSqlCommandBuilder>().Build(sql);
        using var result = command.ExecuteReader(new RelationalCommandParameterObject(
            context.GetService<IRelationalConnection>(), null, null, context,
            context.GetService<IRelationalCommandDiagnosticsLogger>(), CommandSource.FromSqlQuery));
        var reader = result.DbDataReader;
        Assert.True(reader.Read());
        Assert.Equal("read", reader.GetString(1));
        switch (mode)
        {
            case "next-result": reader.NextResult(); break;
            case "close": reader.Close(); break;
            case "dispose": reader.Dispose(); break;
            case "dispose-async": reader.DisposeAsync().AsTask().GetAwaiter().GetResult(); break;
            default: throw new InvalidOperationException("Unknown reader mode.");
        }
    }

    private static object? Scalar(AccountStore store, string sql)
    {
        using var db = store.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    [Theory]
    [MemberData(nameof(Attacks))]
    public async Task LateEfReaderStatementsCannotBorrowCoordinatorAuthority(string sql, string mode, bool swallow)
    {
        using var store = new AccountStore(Options.Create(new ServerOptions { DataDir = _directory }));
        Assert.Equal(1, await store.WriteLegacyAsync<int>(new Seed()));
        var revision = store.ReadSync<OwnerRevisionValue>(new OwnerRevisionReadPlan(RevisionStore.Entity("server_state", "victim")));
        var family = Scalar(store, "SELECT family FROM owner_operation_receipt WHERE operation_id='reader-seed'");
        long deniedBefore = Denials(store);
        if (swallow) Assert.Equal(1, await store.WriteLegacyAsync<int>(new Attack(sql, mode, true)));
        else await Assert.ThrowsAsync<IOException>(() => store.WriteLegacyAsync<int>(new Attack(sql, mode, false)));
        Assert.True(Denials(store) > deniedBefore, "The native guard must actually refuse the late statement.");
        Assert.Equal(0L, Scalar(store, "SELECT count(*) FROM sqlite_master WHERE name='b1_escape'"));
        Assert.Equal("before", await store.ReadAsync<string?>(new SeasonStateReadPlan("victim")));
        Assert.Equal(revision, store.ReadSync<OwnerRevisionValue>(new OwnerRevisionReadPlan(revision.Key)));
        Assert.Equal(family, Scalar(store, "SELECT family FROM owner_operation_receipt WHERE operation_id='reader-seed'"));
        Assert.Equal(swallow ? "committed" : null, await store.ReadAsync<string?>(new SeasonStateReadPlan("allowed")));
        Assert.Equal(swallow ? 1L : 0L, Scalar(store, "SELECT count(*) FROM owner_operation_receipt WHERE operation_id='reader-attack'"));
        Assert.Equal(1, await store.WriteLegacyAsync<int>(new SeasonStateWritePlan("after", "healthy")));
    }

    private static long Denials(AccountStore store) =>
        JsonSerializer.SerializeToElement(store.AccessMetrics.Snapshot()).GetProperty("nonWriterWriteAttempts").GetInt64();

    private sealed class ReadBatch : DbReadPlan
    {
        internal override int Bytes => 1;
        internal override string Label => "test/ef-read-batch";
        internal override object Execute(DbPlanExecution execution)
        {
            Consume(execution.Game, "SELECT 'probe' AS key, 'read' AS value; SELECT 'next' AS key, 'ok' AS value", "next-result");
            return execution.Game.RealmState.AsNoTracking().Count();
        }
    }

    [Fact]
    public async Task NormalEfReadsAndWritesAndReadOnlyResultBatchesStillWork()
    {
        using var store = new AccountStore(Options.Create(new ServerOptions { DataDir = _directory }));
        Assert.Equal(1, await store.WriteLegacyAsync<int>(new SeasonStateWritePlan("ordinary", "same")));
        Assert.Equal(1, await store.WriteLegacyAsync<int>(new SeasonStateWritePlan("ordinary", "same")));
        Assert.Equal("same", await store.ReadAsync<string?>(new SeasonStateReadPlan("ordinary")));
        Assert.True(await store.ReadAsync<int>(new ReadBatch()) > 0);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
