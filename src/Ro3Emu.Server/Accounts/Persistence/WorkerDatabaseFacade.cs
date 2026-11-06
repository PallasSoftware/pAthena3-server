using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Ro3Emu.Server.Accounts.Persistence;

/// <summary>EnsureDeleted can delete a SQLite file without issuing DDL. Keep EF's normal facade,
/// but refuse database lifecycle operations once this context borrows a worker transaction.</summary>
internal sealed class WorkerDatabaseFacade(GameDbContext context) : DatabaseFacade(context)
{
    private void CheckLifecycle()
    {
        if (context.BorrowsWorkerTransaction)
            throw new InvalidOperationException("A worker context cannot create or delete its database.");
    }

    public override bool EnsureCreated() { CheckLifecycle(); return base.EnsureCreated(); }
    public override bool EnsureDeleted() { CheckLifecycle(); return base.EnsureDeleted(); }
    public override Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = default)
    { CheckLifecycle(); return base.EnsureCreatedAsync(cancellationToken); }
    public override Task<bool> EnsureDeletedAsync(CancellationToken cancellationToken = default)
    { CheckLifecycle(); return base.EnsureDeletedAsync(cancellationToken); }
}
