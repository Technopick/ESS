using Ess.Application.Abstractions;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Infrastructure.Services;

/// <summary>Gap-tolerant, concurrency-safe reference numbers using a single atomic upsert in PostgreSQL.</summary>
public sealed class ReferenceGenerator : IReferenceGenerator
{
    private readonly EssDbContext _db;
    private readonly IClock _clock;

    public ReferenceGenerator(EssDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<string> NextAsync(Guid companyId, string prefix, CancellationToken ct = default)
    {
        var year = _clock.UtcNow.Year;
        var next = await _db.Database.SqlQuery<long>($"""
            INSERT INTO reference_sequences (company_id, prefix, year, last_value)
            VALUES ({companyId}, {prefix}, {year}, 1)
            ON CONFLICT (company_id, prefix, year)
            DO UPDATE SET last_value = reference_sequences.last_value + 1
            RETURNING last_value AS "Value"
            """).ToListAsync(ct);
        return $"{prefix}-{year}-{next[0]:D6}";
    }
}
