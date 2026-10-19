using Microsoft.EntityFrameworkCore;
using Npgsql;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.Data;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    // A lost race doesn't always surface as DbUpdateConcurrencyException. EF
    // sends the Booking INSERT before the LessonSlot UPDATE, so the loser can
    // hit a constraint *before* its stale Version is ever checked:
    //   - unique violation: the winner's active booking for the same slot
    //     already exists (IX_Bookings_LessonSlotId_ActiveUnique);
    //   - foreign-key violation: the tutor deleted the slot in the meantime.
    // Both mean "the data changed between our read and our write" - the same
    // thing a concurrency-token mismatch means - so they're reported as one,
    // and the Service layer keeps catching that single specific exception.
    // Translated here because only the Data layer knows the database provider.
    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            return await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && IsConflictingWriteViolation(ex))
        {
            throw new DbUpdateConcurrencyException(
                "The data was changed by another request between read and save.", ex);
        }
    }

    // Virtual so the test suite, which runs on SQLite, can recognize SQLite's
    // equivalent error without production code depending on SQLite.
    protected virtual bool IsConflictingWriteViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation;
}
