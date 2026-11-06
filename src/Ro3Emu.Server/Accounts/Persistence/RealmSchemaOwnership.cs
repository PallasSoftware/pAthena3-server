using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Ro3Emu.Server.Accounts.Persistence;

/// <summary>External, offline-migration policy. Validate before bootstrap writes and return whole tables
/// whose DDL belongs exclusively to that policy. Runtime data backfills remain engine-owned.</summary>
public interface IRealmSchemaPolicy
{
    IReadOnlySet<string> Validate(DbConnection connection);
}

internal static class RealmSchemaOwnership
{
    internal static IReadOnlySet<string> Inspect(string path, IRealmSchemaPolicy? policy)
    {
        if (!File.Exists(path)) return new HashSet<string>(StringComparer.Ordinal);
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open();
        if (policy is not null) return policy.Validate(db);
        using var check = db.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('__EFMigrationsHistory','__GameSchemaOwnership')";
        if ((long)check.ExecuteScalar()! != 0)
            throw new InvalidOperationException("This realm requires an explicit schema compatibility policy before opening.");
        return new HashSet<string>(StringComparer.Ordinal);
    }
}
