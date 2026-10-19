namespace TutorScheduler.Core.Interfaces;

// The only place SaveChangesAsync is called. Repositories only stage changes
// on the tracked context; committing them together is what keeps a booking's
// LessonSlot update and Booking insert in one atomic transaction.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
