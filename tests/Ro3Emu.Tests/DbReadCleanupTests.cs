using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Ro3Emu.Server;
using Ro3Emu.Server.Accounts;
using Ro3Emu.Server.Accounts.Persistence;
using SQLitePCL;

namespace Ro3Emu.Tests;

public sealed class DbReadCleanupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ro3-read-cleanup-" + Guid.NewGuid().ToString("N"));
    private sealed class BlockedRead(CountdownEvent entered, ManualResetEventSlim release, bool close) : DbReadPlan
    {
        internal override int Bytes => 1;
        internal override string Label => "test/read-cleanup";
        internal override object Execute(SqliteConnection db, SqliteTransaction tx)
        {
            entered.Signal();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic reader barrier.");
            if (close)
            {
                // Deliberately simulate a lost connection. Disarm the authorizer only on this test's
                // disposable reader, so Close can finish inside the plan's transaction fence.
                Assert.Equal(raw.SQLITE_OK, raw.sqlite3_set_authorizer(
                    db.Handle ?? throw new InvalidOperationException("Fixture connection closed."),
                    (delegate_authorizer)((_, _, _, _, _, _) => raw.SQLITE_OK), null));
                db.Close();
            }
            return 1;
        }
    }

    [Fact]
    public async Task CleanupFailureCompletesTheDequeuedJobAndFailsQueuedAndFutureWork()
    {
        using var store = new AccountStore(Options.Create(new ServerOptions { DataDir = _directory }));
        string path;
        using (var db = store.Open()) path = db.DataSource;
        using var pool = new DbReadPool(path, new DbAccessMetrics());
        using var entered = new CountdownEvent(2);
        using var releaseFailure = new ManualResetEventSlim();
        using var releaseOther = new ManualResetEventSlim();
        var failing = pool.ExecuteAsync<int>(new BlockedRead(entered, releaseFailure, true));
        var other = pool.ExecuteAsync<int>(new BlockedRead(entered, releaseOther, false));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var queued = pool.ExecuteAsync<string?>(new SeasonStateReadPlan("unused"));
            releaseFailure.Set();
            Assert.Same(failing, await Task.WhenAny(failing, Task.Delay(TimeSpan.FromSeconds(5))));
            await Assert.ThrowsAsync<AggregateException>(() => failing);
            await Assert.ThrowsAnyAsync<Exception>(() => queued.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(queued.IsFaulted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => pool.ExecuteAsync<string?>(new SeasonStateReadPlan("refused")));
        }
        finally { releaseFailure.Set(); releaseOther.Set(); }
        Assert.Equal(1, await other.WaitAsync(TimeSpan.FromSeconds(5)));
        await pool.WorkersExited.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
