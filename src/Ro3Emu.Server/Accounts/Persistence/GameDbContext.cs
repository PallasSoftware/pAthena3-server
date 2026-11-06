using System.Data.Common;
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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(new EfCoordinationGuard());

    internal IReadOnlySet<string>? AllowedRealmWrites { get; set; }

    internal void ValidateWrites()
    {
        ChangeTracker.DetectChanges();
        if (AllowedRealmWrites is { } allowed && ChangeTracker.Entries<RealmStateRow>().Any(entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && !allowed.Contains(entry.Entity.Key)))
            throw new InvalidOperationException("EF attempted an undeclared realm-state mutation.");
        if (ChangeTracker.Entries().Any(entry => entry.Entity is OwnerRevisionRow or OwnerReceiptRow
            && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Revisions and receipts belong to the transaction coordinator, not EF plans.");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Enlist without creating a provider or owning the worker's transaction.</summary>
    internal static GameDbContext Borrow(DbConnection connection, DbTransaction transaction,
        IDbContextFactory<GameDbContext> factory)
    {
        var context = factory.CreateDbContext();
        try
        {
            context.Database.SetDbConnection(connection, contextOwnsConnection: false);
            context.Database.UseTransaction(transaction);
            context.Database.AutoSavepointsEnabled = false;
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
        // SQL Server pads trailing spaces for equality and bounds nvarchar keys by UTF-16 units.
        // Keep SQLite's existing key semantics; this is not an automatic cross-provider conversion.
        if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer" && (key.Length > 256 || key.EndsWith(' ')))
            throw new ArgumentException("Realm-state key is not lossless on SQL Server.", nameof(key));
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
