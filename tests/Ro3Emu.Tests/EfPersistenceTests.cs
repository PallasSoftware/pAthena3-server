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
        internal override object Execute(DbPlanExecution execution)
        {
            var context = execution.Game;
            return context.Database.ExecuteSqlRaw("UPDATE server_state SET value='forbidden'");
        }
    }

    private sealed class UndeclaredWrite : DbPlan
    {
        internal override int Bytes => 1;
        internal override string Label => "test/ef-undeclared-mutation";
        internal override object Execute(DbPlanExecution execution)
        {
            var context = execution.Game;
            return context.Database.ExecuteSqlRaw("UPDATE role SET level=2");
        }
    }

    [Fact]
    public async Task EfCannotBypassTheNativeReadOnlyAndRoleMutationAuthorizers()
    {
        using var store = Store();
        var read = await Assert.ThrowsAsync<SqliteException>(() => store.ReadAsync<int>(new ReadWrite()));
        Assert.Equal(23, read.SqliteErrorCode);
        var write = await Assert.ThrowsAsync<SqliteException>(() => store.WriteAsync<int>(new UndeclaredWrite()));
        Assert.Equal(23, write.SqliteErrorCode);
    }

    [Fact]
    public async Task MissingWorkerFactoryRefusesEfInsteadOfSelectingSqlite()
    {
        using var store = Store();
        using var writer = new DbWriter("Data Source=" + store.DatabasePath, new DbAccessMetrics());
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ExecuteAsync<int>(
            new SeasonStateWritePlan("missing-factory", "never")));
        using var readers = new DbReadPool(store.DatabasePath, new DbAccessMetrics());
        await Assert.ThrowsAsync<InvalidOperationException>(() => readers.ExecuteAsync<string?>(
            new SeasonStateReadPlan("missing-factory")));
    }

    private sealed class ForbiddenCoordination(string mutation) : DbPlan
    {
        internal override int Bytes => 1;
        internal override string Label => "test/ef-coordination-denied";
        internal override object Execute(DbPlanExecution execution)
        {
            var context = execution.Game;
            switch (mutation)
            {
                case "tracked-revision":
                    context.OwnerRevisions.Add(new OwnerRevisionRow { Kind = "bad", Id = "bad", Part = "state", Revision = 1 });
                    return context.SaveChanges();
                case "tracked-receipt":
                    context.OwnerReceipts.Add(new OwnerReceiptRow { OperationId = "bad", Family = "bad", Result = [1] });
                    return context.SaveChanges();
                case "bulk-revision": return context.OwnerRevisions.ExecuteDelete();
                case "bulk-receipt": return context.OwnerReceipts.ExecuteDelete();
                case "raw-revision": return context.Database.ExecuteSqlRaw("DELETE FROM owner_revision");
                case "raw-receipt": return context.Database.ExecuteSqlRaw("DELETE FROM owner_operation_receipt");
                default: throw new InvalidOperationException("Unknown test mutation.");
            }
        }
    }

    [Theory]
    [InlineData("tracked-revision")]
    [InlineData("tracked-receipt")]
    [InlineData("bulk-revision")]
    [InlineData("bulk-receipt")]
    [InlineData("raw-revision")]
    [InlineData("raw-receipt")]
    public async Task EfCannotWriteCoordinatorTablesThroughTrackingBulkOrRawSql(string mutation)
    {
        using var store = Store();
        await Write(store, "before");
        long before = Revision(store).Revision;
        var error = await Record.ExceptionAsync(() => store.WriteAsync<int>(new ForbiddenCoordination(mutation)));
        Assert.NotNull(error);
        if (mutation.StartsWith("tracked-", StringComparison.Ordinal)) Assert.IsType<InvalidOperationException>(error);
        else Assert.Equal(23, Assert.IsType<SqliteException>(error).SqliteErrorCode);
        Assert.Equal(before, Revision(store).Revision);
        Assert.Equal(1, await Write(store, "after")); // failed EF permissions must not leak into coordinator writes
        Assert.Equal(before + 1, Revision(store).Revision);
    }

    [Fact]
    public async Task EconomyEnvelopeUsesTheRegisteredFactoryAndRollsBackAllChildrenOnEfFailure()
    {
        var probe = new SaveProbe();
        var factory = new ProbeFactory(probe);
        using var store = Store(factory);
        var roles = new RoleStore(store);
        long role = roles.Create("invented-owner", 1, "EfEnvelope", 1, 1000, 1)!.Id;
        EconomyEnvelopePlan Plan(string operation)
        {
            var child = new RoleItemPlan([new SeasonStateWritePlan("season_id", "1"),
                new SeasonStateWritePlan("playbook", "2")], "test/ef-child");
            // Integration fixture only: season writes are deliberately not added to the production
            // economy intent allowlist. The envelope still executes its real transaction/receipt logic.
            var envelope = new Ro3Emu.Server.Economy.EconomyEnvelope(operation, "invented-owner", role,
                "ef-metadata", "invented-intent", operation + ":source", 1000, [], [1]).BindChildIntent([1]);
            var plan = new EconomyEnvelopePlan(child, envelope, store.EconomyCatalog);
            plan.ExpectEntities(child.EntityWrites.Select(key => store.ReadSync<OwnerRevisionValue>(new OwnerRevisionReadPlan(key))));
            return plan;
        }
        probe.FailAfterSave = true;
        probe.FailOnSave = 2;
        await Assert.ThrowsAsync<IOException>(() => store.WriteAsync<object?[]>(Plan("rollback")));
        Assert.Equal(1, factory.Created); // both EF children shared exactly one context
        Assert.Null(await store.ReadAsync<string?>(new SeasonStateReadPlan("playbook")));
        Assert.False(store.ReadSync<OwnerRevisionValue>(new OwnerRevisionReadPlan(RevisionStore.Entity("server_state", "playbook"))).Exists);
        probe.FailAfterSave = false;
        var plan = Plan("commit");
        await store.WriteAsync<object?[]>(plan);
        Assert.Equal("2", await store.ReadAsync<string?>(new SeasonStateReadPlan("playbook")));
        Assert.Equal(1, store.ReadSync<OwnerRevisionValue>(new OwnerRevisionReadPlan(RevisionStore.Entity("server_state", "playbook"))).Revision);
    }

    [Fact]
    public async Task EconomyEnvelopeCannotBypassARefusingFactory()
    {
        using var store = Store(new RefusingFactory());
        var roles = new RoleStore(store);
        long role = roles.Create("invented-owner", 1, "RefuseEf", 1, 1000, 1)!.Id;
        var child = new RoleItemPlan([new SeasonStateWritePlan("invented-season", "never")], "test/ef-child");
        var envelope = new Ro3Emu.Server.Economy.EconomyEnvelope("refused", "invented-owner", role,
            "ef-metadata", "invented-intent", "refused:source", 1000, [], [1]).BindChildIntent([1]);
        var plan = new EconomyEnvelopePlan(child, envelope, store.EconomyCatalog);
        plan.ExpectEntities([Revision(store)]);
        await Assert.ThrowsAsync<IOException>(() => store.WriteAsync<object?[]>(plan));
        Assert.False(Revision(store).Exists);
    }

    private sealed class UndeclaredRealmWrite(string mode) : DbPlan
    {
        internal override int Bytes => 1;
        internal override string Label => "test/ef-undeclared-key";
        internal override IReadOnlyList<OwnerRevisionKey> EntityWrites => [RevisionStore.Entity("server_state", "declared")];
        internal override object Execute(DbPlanExecution execution)
        {
            var context = execution.Game;
            return mode switch
            {
                "tracked" => RealmStatePersistence.Write(context, "other", "forbidden"),
                "bulk" => context.RealmState.ExecuteDelete(),
                "raw" => context.Database.ExecuteSqlRaw("DELETE FROM server_state"),
                "thread" => Task.Run(() => RealmStatePersistence.Write(context, "declared", "forbidden")).GetAwaiter().GetResult(),
                _ => throw new InvalidOperationException("Unknown test mode.")
            };
        }
    }

    [Theory]
    [InlineData("tracked")]
    [InlineData("bulk")]
    [InlineData("raw")]
    [InlineData("thread")]
    public async Task EfRealmWritesMustMatchThePlanAndStayOnTheWriterThread(string mode)
    {
        using var store = Store();
        await Write(store, "preserve");
        var error = await Record.ExceptionAsync(() => store.WriteLegacyAsync<int>(new UndeclaredRealmWrite(mode)));
        if (mode == "tracked") Assert.IsType<InvalidOperationException>(error);
        else
        {
            var native = error is DbUpdateException wrapped ? wrapped.InnerException : error;
            Assert.Equal(23, Assert.IsType<SqliteException>(native).SqliteErrorCode);
        }
        Assert.Equal("preserve", await Read(store));
        Assert.False(store.ReadSync<OwnerRevisionValue>(new OwnerRevisionReadPlan(RevisionStore.Entity("server_state", "declared"))).Exists);
    }

    private sealed class ProbeFactory(SaveProbe probe) : IDbContextFactory<GameDbContext>
    {
        private int _created;
        public int Created => Volatile.Read(ref _created);
        public GameDbContext CreateDbContext()
        {
            Interlocked.Increment(ref _created);
            return new(new DbContextOptionsBuilder<GameDbContext>().UseSqlite().AddInterceptors(probe, new NoEfSavepoints()).Options);
        }
    }

    private sealed class RefusingFactory : IDbContextFactory<GameDbContext>
    {
        public GameDbContext CreateDbContext() => throw new IOException("Invented EF factory failure.");
    }

    private sealed class NoEfSavepoints : DbTransactionInterceptor
    {
        public override InterceptionResult CreatingSavepoint(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result) => throw new InvalidOperationException("Only the writer may create savepoints.");
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
            Assert.False(eventData.Context.Database.AutoSavepointsEnabled);
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
