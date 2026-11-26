using Ess.Application.Abstractions;
using Ess.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ess.Infrastructure.Persistence;

/// <summary>Stamps CreatedBy/ModifiedBy/UTC timestamps and turns hard deletes of auditable rows into soft deletes.</summary>
public sealed class AuditStampInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public AuditStampInterceptor(ICurrentUser user, IClock clock)
    {
        _user = user;
        _clock = clock;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? db)
    {
        if (db is null) return;
        var now = _clock.UtcNow;
        var who = _user.UserName ?? "system";
        foreach (var entry in db.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedBy ??= who;
                    break;
                case EntityState.Modified:
                    entry.Entity.ModifiedAtUtc = now;
                    entry.Entity.ModifiedBy = who;
                    entry.Property(x => x.CreatedAtUtc).IsModified = false;
                    entry.Property(x => x.CreatedBy).IsModified = false;
                    break;
                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAtUtc = now;
                    entry.Entity.DeletedBy = who;
                    break;
            }
        }
    }
}
