using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ro3Emu.Server.Accounts.Persistence;

/// <summary>The EF Core PoC owns three complete tables, never a partial mapping of role or item.
/// Runtime contexts borrow the writer/reader's guarded connection and transaction; migrations run offline.</summary>
public sealed class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<RealmStateRow> RealmState => Set<RealmStateRow>();
    public DbSet<OwnerRevisionRow> OwnerRevisions => Set<OwnerRevisionRow>();
    public DbSet<OwnerReceiptRow> OwnerReceipts => Set<OwnerReceiptRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        string collation = Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer"
            ? "Latin1_General_100_BIN2" : "BINARY";
        var state = modelBuilder.Entity<RealmStateRow>();
        state.ToTable("server_state");
        state.HasKey(row => row.Key);
        state.Property(row => row.Key).HasColumnName("key").HasMaxLength(256).UseCollation(collation).ValueGeneratedNever();
        state.Property(row => row.Value).HasColumnName("value").IsRequired();

        var revision = modelBuilder.Entity<OwnerRevisionRow>();
        revision.ToTable("owner_revision", table => table.HasCheckConstraint("CK_owner_revision_nonnegative", "revision >= 0"));
        revision.HasKey(row => new { row.Kind, row.Id, row.Part });
        revision.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(32).UseCollation(collation);
        revision.Property(row => row.Id).HasColumnName("id").HasMaxLength(256).UseCollation(collation);
        revision.Property(row => row.Part).HasColumnName("part").HasMaxLength(96).UseCollation(collation);
        revision.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();

        var receipt = modelBuilder.Entity<OwnerReceiptRow>();
        receipt.ToTable("owner_operation_receipt");
        receipt.HasKey(row => row.OperationId);
        receipt.Property(row => row.OperationId).HasColumnName("operation_id").HasMaxLength(256).UseCollation(collation).ValueGeneratedNever();
        receipt.Property(row => row.Family).HasColumnName("family").IsRequired();
        receipt.Property(row => row.Result).HasColumnName("result").IsRequired();
    }

    /// <summary>Attach to exactly the existing native handle, without owning or committing its transaction.
    /// A fresh context per plan also discards tracked state after savepoint rollback.</summary>
    internal static GameDbContext Borrow(SqliteConnection connection, SqliteTransaction transaction,
        IDbContextFactory<GameDbContext>? factory = null)
    {
        var context = factory?.CreateDbContext() ?? new GameDbContext(
            new DbContextOptionsBuilder<GameDbContext>().UseSqlite().Options);
        try
        {
            context.Database.SetDbConnection(connection, contextOwnsConnection: false);
            context.Database.UseTransaction(transaction);
            return context;
        }
        catch { context.Dispose(); throw; }
    }
}

public sealed class RealmStateRow
{
    public required string Key { get; set; }
    public required string Value { get; set; }
}

public sealed class OwnerRevisionRow
{
    public required string Kind { get; set; }
    public required string Id { get; set; }
    public required string Part { get; set; }
    public long Revision { get; set; }
}

public sealed class OwnerReceiptRow
{
    public required string OperationId { get; set; }
    public required string Family { get; set; }
    public required byte[] Result { get; set; }
}

/// <summary>One provider-independent EF route. Existing revision/receipt/owner coordination stays outside the ORM.</summary>
public static class RealmStatePersistence
{
    public static string? Read(GameDbContext context, string key) =>
        context.RealmState.AsNoTracking().Where(row => row.Key == key).Select(row => row.Value).SingleOrDefault();

    public static int Write(GameDbContext context, string key, string value)
    {
        var row = context.RealmState.Find(key);
        if (row is null) context.RealmState.Add(new RealmStateRow { Key = key, Value = value });
        else
        {
            row.Value = value;
            // SQLite's old upsert reports one affected row even for an identical value; revisions must still advance.
            context.Entry(row).Property(state => state.Value).IsModified = true;
        }
        return context.SaveChanges();
    }
}
