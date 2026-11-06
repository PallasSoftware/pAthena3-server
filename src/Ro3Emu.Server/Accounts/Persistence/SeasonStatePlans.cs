using Microsoft.Data.Sqlite;

namespace Ro3Emu.Server.Accounts.Persistence;

internal sealed class SeasonStateSchemaPlan : DbPlan
{
    internal override int Bytes => 128;
    internal override string Label => "season/schema";
    internal override object Execute(SqliteConnection db, SqliteTransaction tx)
    {
        using var cmd = DbSql.Command(db, tx,
            "CREATE TABLE IF NOT EXISTS server_state (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
        int changed = cmd.ExecuteNonQuery();
        SchemaSql.Execute(db, tx, """
            INSERT OR IGNORE INTO owner_revision(kind,id,part,revision)
                SELECT 'server_state',key,'state',0 FROM server_state
            """);
        return changed;
    }
}

internal sealed class SeasonStateReadPlan(string key) : DbReadPlan
{
    internal override int Bytes => 128 + key.Length * 2;
    internal override string Label => "season/read";
    internal override object? Execute(DbPlanExecution execution) =>
        RealmStatePersistence.Read(execution.Game, key);
}

internal sealed class SeasonStateWritePlan(string key, string value) : DbPlan
{
    internal override int Bytes => 128 + (key.Length + value.Length) * 2;
    internal override string Label => "season/write";
    internal override IReadOnlyList<OwnerRevisionKey> EntityWrites => [RevisionStore.Entity("server_state", key)];
    internal override object Execute(DbPlanExecution execution) =>
        RealmStatePersistence.Write(execution.Game, key, value);
}
