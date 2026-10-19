using TutorScheduler.Core.Entities;

namespace TutorScheduler.Core.Interfaces;

public interface IBookingRepository
{
    // Tracked: needed for the cancel flow, which also mutates a LessonSlot.
    Task<Booking?> GetByIdAsync(int id, CancellationToken ct);

    // Read-only listing: AsNoTracking, real Skip/Take against the database.
    Task<(IReadOnlyList<Booking> Items, int TotalCount)> GetByStudentPagedAsync(
        int studentId, int page, int pageSize, CancellationToken ct);

    // AsNoTracking: for Controller-facing detail reads, distinct from the
    // tracked GetByIdAsync above which is only ever used for mutation.
    Task<Booking?> GetDetailByIdAsync(int id, CancellationToken ct);

    // Tracked: the tutor-cancel flow marks this booking cancelled.
    Task<Booking?> GetActiveForSlotAsync(int lessonSlotId, CancellationToken ct);

    void Add(Booking booking);

    // Used by LessonSlot deletion: a slot with any booking history (even a
    // cancelled one) must be soft-deleted, not hard-deleted, or the cascade
    // delete on Bookings.LessonSlotId would erase that history.
    Task<bool> AnyForSlotAsync(int lessonSlotId, CancellationToken ct);

    // A student can't be in two lessons at once: used before booking to reject
    // a slot that overlaps any of the student's own existing active bookings.
    Task<bool> HasOverlappingActiveBookingAsync(
        int studentId, DateTime startUtc, DateTime endUtc, CancellationToken ct);
}
