using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ro3Emu.Server;
using Ro3Emu.Server.Accounts;
using Ro3Emu.Server.Accounts.Persistence;

namespace Ro3Emu.Tests;

public sealed class EfPersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ro3-ef-" + Guid.NewGuid().ToString("N"));
    private AccountStore Store(IDbContextFactory<GameDbContext>? contexts = null) =>
        new(Options.Create(new ServerOptions { DataDir = _dir, GameDataDir = SyntheticData.Dir }), contexts: contexts);
    private static Task<int> Write(AccountStore store, string value) =>
        store.WriteLegacyAsync<int>(new SeasonStateWritePlan("invented-season", value));
    private static Task<string?> Read(AccountStore store) => store.ReadAsync<string?>(new SeasonStateReadPlan("invented-season"));
    private static OwnerRevisionValue Revision(AccountStore store) => store.ReadSync<OwnerRevisionValue>(
        new OwnerRevisionReadPlan(RevisionStore.Entity("server_state", "invented-season")));

    [Fact]
    public async Task DiFactoryRunsExistingRealmStateRouteThroughEfOnTheGuardedTransaction()
    {
        var probe = new SaveProbe();
        var factory = new ProbeFactory(probe);
        await using var app = ServerHost.Create([], o => { o.DataDir = _dir; o.GameDataDir = SyntheticData.Dir; },
            configureServices: services => services.AddSingleton<IDbContextFactory<GameDbContext>>(factory));
        var store = app.Services.GetRequiredService<AccountStore>();
        Assert.Same(factory, app.Services.GetRequiredService<IDbContextFactory<GameDbContext>>());
        Assert.Null(await Read(store));
        Assert.Equal(1, await Write(store, "first"));
        Assert.Equal("first", await Read(store));
        long before = Revision(store).Revision;
        Assert.Equal(1, await Write(store, "first")); // identical upserts still bump, as before EF
        Assert.Equal(before + 1, Revision(store).Revision);
        Assert.True(factory.Created >= 4);
        Assert.True(probe.Saves >= 2);
        Assert.True(probe.Enlisted);
    }

    [Fact]
    public async Task ContextFactoryFailureCannotFallBackToLegacySql()
    {
        using var store = Store(new RefusingFactory());
        await Assert.ThrowsAsync<IOException>(() => Write(store, "never"));
        await Assert.ThrowsAsync<IOException>(() => Read(store));
        Assert.False(Revision(store).Exists);
    }

    [Fact]
    public async Task FailureAfterSaveChangesRollsBackDataAndRevisionAndTheNextContextIsClean()
    {
        var probe = new SaveProbe();
        using var store = Store(new ProbeFactory(probe));
        Assert.Equal(1, await Write(store, "before"));
        long before = Revision(store).Revision;
        probe.FailAfterSave = true;
        await Assert.ThrowsAsync<IOException>(() => Write(store, "rolled-back"));
        Assert.Equal("before", await Read(store));
        Assert.Equal(before, Revision(store).Revision);
        probe.FailAfterSave = false;
        Assert.Equal(1, await Write(store, "after"));
        Assert.Equal("after", await Read(store));
        Assert.Equal(before + 1, Revision(store).Revision);
    }

    [Fact]
    public async Task StaleRevisionIsRejectedBeforeEfWritesAnything()
    {
        using var store = Store();
        await Write(store, "one");
        var stale = new SeasonStateWritePlan("invented-season", "stale");
        stale.ExpectEntities([Revision(store)]);
        await Write(store, "two");
        await Assert.ThrowsAsync<OwnerRevisionConflictException>(() => store.WriteAsync<int>(stale));
        Assert.Equal("two", await Read(store));
    }

    [Fact]
    public async Task SaveChangesDoesNotCommitOrReleaseTheReplyBeforeTheWriterCommit()
    {
        using var store = Store();
        await Write(store, "before");
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        store.BeforeCommitForTest = label =>
        {
            if (label != "season/write") return;
            reached.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Commit barrier timed out.");
        };
        var write = Write(store, "after");
        try
        {
            Assert.True(reached.Wait(TimeSpan.FromSeconds(10)));
            Assert.False(write.IsCompleted);
            Assert.Equal("before", await Read(store));
        }
        finally { release.Set(); }
        Assert.Equal(1, await write);
        Assert.Equal("after", await Read(store));
    }

    [Fact]
    public async Task LostAckAndReplayUseTheExistingOwnerReceiptWithoutAnotherEfSave()
    {
        var probe = new SaveProbe();
        using var store = Store(new ProbeFactory(probe));
        var before = Revision(store);
        RoleItemPlan Plan()
        {
            var plan = new RoleItemPlan([new SeasonStateWritePlan("invented-season", "once")], "test/ef-owned",
                operationId: "invented-ef-operation");
            plan.ExpectEntities([before]);
            return plan;
        }
        store.AfterWriterCommitForTest = label =>
        {
            if (label == "test/ef-owned") throw new IOException("Invented lost acknowledgement.");
        };
        Assert.Equal(1, (await store.WriteAsync<object?[]>(Plan()))[0]);
        long revision = Revision(store).Revision;
        Assert.Equal(1, (await store.WriteAsync<object?[]>(Plan()))[0]);
        Assert.Equal(revision, Revision(store).Revision);
        Assert.Equal("once", await Read(store));
        Assert.Equal(1, probe.Saves);
    }

    [Fact]
    public async Task EfFailureRollsBackTheWholeCompositeIncludingEarlierChildren()
    {
        var probe = new SaveProbe { FailAfterSave = true };
        using var store = Store(new ProbeFactory(probe));
        var first = new SeasonStateWritePlan("invented-first", "one");
        var second = new SeasonStateWritePlan("invented-season", "two");
        var plan = new RoleItemPlan([first, second], "test/ef-atomic");
        // Fail the second SaveChanges, after the first child's rows already exist in the transaction.
        probe.FailOnSave = 2;
        await Assert.ThrowsAsync<IOException>(() => store.WriteLegacyAsync<object?[]>(plan));
        Assert.Null(await store.ReadAsync<string?>(new SeasonStateReadPlan("invented-first")));
        Assert.Null(await Read(store));
        Assert.False(Revision(store).Exists);
    }

    private sealed class ReadWrite : DbReadPlan
    {
        internal override int Bytes => 1;
        internal override string Label => "test/ef-read-mutation";
        internal override object Execute(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var context = GameDbContext.Borrow(connection, transaction);
            return RealmStatePersistence.Write(context, "forbidden", "value");
        }
    }

    private sealed class UndeclaredWrite : DbPlan
    {
        internal override int Bytes => 1;
        internal override string Label => "test/ef-undeclared-mutation";
        internal override object Execute(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var context = GameDbContext.Borrow(connection, transaction);
            return context.Database.ExecuteSqlRaw("UPDATE role SET level=2");
        }
    }

    [Fact]
    public async Task EfCannotBypassTheNativeReadOnlyAndRoleMutationAuthorizers()
    {
        using var store = Store();
        var read = await Assert.ThrowsAsync<DbUpdateException>(() => store.ReadAsync<int>(new ReadWrite()));
        Assert.Equal(23, Assert.IsType<SqliteException>(read.InnerException).SqliteErrorCode);
        var write = await Assert.ThrowsAsync<SqliteException>(() => store.WriteAsync<int>(new UndeclaredWrite()));
        Assert.Equal(23, write.SqliteErrorCode);
    }

    private sealed class ProbeFactory(SaveProbe probe) : IDbContextFactory<GameDbContext>
    {
        private int _created;
        public int Created => Volatile.Read(ref _created);
        public GameDbContext CreateDbContext()
        {
            Interlocked.Increment(ref _created);
            return new(new DbContextOptionsBuilder<GameDbContext>().UseSqlite().AddInterceptors(probe).Options);
        }
    }

    private sealed class RefusingFactory : IDbContextFactory<GameDbContext>
    {
        public GameDbContext CreateDbContext() => throw new IOException("Invented EF factory failure.");
    }

    private sealed class SaveProbe : SaveChangesInterceptor
    {
        private int _saves;
        public int Saves => Volatile.Read(ref _saves);
        public bool Enlisted { get; private set; }
        public bool FailAfterSave { get; set; }
        public int FailOnSave { get; set; } = 1;
        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            Enlisted = eventData.Context!.Database.CurrentTransaction is not null;
            int count = Interlocked.Increment(ref _saves);
            if (FailAfterSave && count >= FailOnSave) throw new IOException("Invented failure after EF SQL.");
            return result;
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
