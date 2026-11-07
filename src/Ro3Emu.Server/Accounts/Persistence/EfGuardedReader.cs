using System.Collections;
using System.Data;
using System.Data.Common;

namespace Ro3Emu.Server.Accounts.Persistence;

/// <summary>SQLite prepares later batch statements during reader advancement and Close/Dispose.
/// Scope authority around each provider operation, restoring it even on failure; never leave it on
/// between calls or while coordinator/other commands run. Offline readers are not wrapped.</summary>
internal sealed class EfGuardedReader(DbDataReader inner, DbConnection? connection, bool tracked) : DbDataReader
{
    private T Guard<T>(Func<T> action)
    {
        using var authority = DbWriteGuard.EfReader(connection, tracked);
        return action();
    }
    private void Guard(Action action) => Guard(() => { action(); return 0; });

    public override bool Read() => Guard(inner.Read);
    public override bool NextResult() => Guard(inner.NextResult);
    public override void Close() => Guard(() =>
    {
        // SqliteDataReader.Close drains remaining statements but suppresses their errors. Advance
        // explicitly so authorization failure reaches the caller, then close under the same guard.
        try { while (!inner.IsClosed && inner.NextResult()) { } }
        finally { inner.Close(); }
    });
    // DbDataReader's Dispose, DisposeAsync and CloseAsync dispatch through this guarded Close.
    // SQLite cleanup stays synchronous; authority never crosses an await continuation.
    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    { DbWriteGuard.RefuseAsyncEfCommand(connection); return inner.ReadAsync(cancellationToken); }
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
    { DbWriteGuard.RefuseAsyncEfCommand(connection); return inner.NextResultAsync(cancellationToken); }

    public override object this[int ordinal] => inner[ordinal];
    public override object this[string name] => inner[name];
    public override int Depth => inner.Depth;
    public override int FieldCount => inner.FieldCount;
    public override int VisibleFieldCount => inner.VisibleFieldCount;
    public override bool HasRows => inner.HasRows;
    public override bool IsClosed => inner.IsClosed;
    public override int RecordsAffected => inner.RecordsAffected;
    // Do not return the inner enumerator: it would bypass this reader's guarded Read.
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);
    public override DataTable? GetSchemaTable() => inner.GetSchemaTable();
    public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
    public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
    public override long GetBytes(int ordinal, long offset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, offset, buffer, bufferOffset, length);
    public override char GetChar(int ordinal) => inner.GetChar(ordinal);
    public override long GetChars(int ordinal, long offset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, offset, buffer, bufferOffset, length);
    public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
    public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
    public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
    public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
    public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
    public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
    public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
    public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
    public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
    public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
    public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
    public override string GetName(int ordinal) => inner.GetName(ordinal);
    public override int GetOrdinal(string name) => inner.GetOrdinal(name);
    public override string GetString(int ordinal) => inner.GetString(ordinal);
    public override object GetValue(int ordinal) => inner.GetValue(ordinal);
    public override int GetValues(object[] values) => inner.GetValues(values);
    public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
}
