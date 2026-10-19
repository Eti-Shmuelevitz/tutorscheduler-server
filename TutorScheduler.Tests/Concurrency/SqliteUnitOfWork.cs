using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TutorScheduler.Data;

namespace TutorScheduler.Tests.Concurrency;

// The production UnitOfWork, with only the provider-specific error check
// swapped for SQLite's: everything else under test is the real code.
public class SqliteUnitOfWork : UnitOfWork
{
    private const int SqliteConstraint = 19;

    public SqliteUnitOfWork(AppDbContext context) : base(context)
    {
    }

    protected override bool IsConflictingWriteViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraint };
}
