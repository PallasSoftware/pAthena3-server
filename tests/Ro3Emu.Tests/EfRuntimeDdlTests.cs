using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ro3Emu.Server;
using Ro3Emu.Server.Accounts;
using Ro3Emu.Server.Accounts.Persistence;

namespace Ro3Emu.Tests;

public sealed class EfRuntimeDdlTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ro3-ef-ddl-" + Guid.NewGuid().ToString("N"));
    private sealed class Ddl(string sql) : DbPlan
    {
        internal override int Bytes => 128;
        internal override string Label => "test/ef-ddl";
        internal override string OperationId => "synthetic-ddl";
        internal override IReadOnlyList<OwnerRevisionKey> EntityWrites => [RevisionStore.Entity("server_state", "hardening")];
        internal override object Execute(DbPlanExecution execution)
        {
            RealmStatePersistence.Write(execution.Game, "hardening", "tentative");
            return execution.Game.Database.ExecuteSqlRaw(sql);
        }
    }

    private static List<string> Schema(AccountStore store)
    {
        using var db = store.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT type||':'||name||':'||coalesce(sql,'') FROM sqlite_master ORDER BY name";
        using var rows = command.ExecuteReader();
        var result = new List<string>();
        while (rows.Read()) result.Add(rows.GetString(0));
        return result;
    }

    [Theory]
    [InlineData("CREATE TABLE denied(value TEXT)")]
    [InlineData("ALTER TABLE server_state ADD COLUMN denied TEXT")]
    [InlineData("ALTER TABLE server_state RENAME TO denied")]
    [InlineData("ALTER TABLE server_state RENAME COLUMN value TO denied")]
    [InlineData("DROP TABLE server_state")]
    [InlineData("CREATE INDEX denied ON server_state(value)")]
    [InlineData("DROP INDEX hardening_state_index")]
    [InlineData("CREATE TRIGGER denied AFTER INSERT ON server_state BEGIN SELECT 1; END")]
    [InlineData("DROP TRIGGER hardening_state_trigger")]
    [InlineData("CREATE VIEW denied AS SELECT * FROM server_state")]
    [InlineData("DROP VIEW hardening_state_view")]
    [InlineData("CREATE TEMP TABLE denied(value TEXT)")]
    [InlineData("CREATE TEMP TRIGGER denied AFTER INSERT ON main.server_state BEGIN SELECT 1; END")]
    [InlineData("PRAGMA writable_schema=ON")]
    [InlineData("ATTACH DATABASE ':memory:' AS denied")]
    [InlineData("ANALYZE server_state")]
    [InlineData("REINDEX hardening_state_index")]
    [InlineData("DROP TABLE owner_operation_receipt")]
    [InlineData("CREATE UNIQUE INDEX denied ON owner_operation_receipt(family)")]
    [InlineData("DROP INDEX hardening_revision_index")]
    [InlineData("CREATE TRIGGER denied AFTER INSERT ON owner_operation_receipt BEGIN SELECT 1; END")]
    public async Task AllEfRuntimeSchemaMutationIsRefusedAndTheWriterCanContinue(string sql)
    {
        using var store = new AccountStore(Options.Create(new ServerOptions { DataDir = _directory }));
        FixtureSql.Execute(store, """
            CREATE INDEX hardening_state_index ON server_state(value);
            CREATE INDEX hardening_revision_index ON owner_revision(id);
            CREATE TRIGGER hardening_state_trigger AFTER INSERT ON server_state BEGIN SELECT 1; END;
            CREATE VIEW hardening_state_view AS SELECT * FROM server_state;
            """);
        await store.WriteLegacyAsync<int>(new SeasonStateWritePlan("hardening", "before"));
        var schema = Schema(store);
        var revision = store.ReadSync<OwnerRevisionValue>(new OwnerRevisionReadPlan(RevisionStore.Entity("server_state", "hardening")));
        var error = await Assert.ThrowsAsync<SqliteException>(() => store.WriteLegacyAsync<int>(new Ddl(sql)));
        Assert.Equal(23, error.SqliteErrorCode);
        Assert.Equal(schema, Schema(store));
        Assert.Equal("before", await store.ReadAsync<string?>(new SeasonStateReadPlan("hardening")));
        Assert.Equal(revision, store.ReadSync<OwnerRevisionValue>(new OwnerRevisionReadPlan(revision.Key)));
        using (var db = store.Open())
        using (var command = db.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM owner_operation_receipt WHERE operation_id='synthetic-ddl'";
            Assert.Equal(0L, command.ExecuteScalar());
        }
        Assert.Equal(1, await store.WriteLegacyAsync<int>(new SeasonStateWritePlan("after", "healthy")));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
