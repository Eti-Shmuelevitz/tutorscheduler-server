using TutorScheduler.Core.Entities;

namespace TutorScheduler.Core.Interfaces;

public interface ILessonSlotRepository
{
    // Tracked (no AsNoTracking): the booking flow mutates Status on the
    // returned entity directly, then commits via IUnitOfWork so the
    // concurrency check on Version can run.
    Task<LessonSlot?> GetByIdAsync(int id, CancellationToken ct);

    // AsNoTracking: for Controller-facing detail reads, distinct from the
    // tracked GetByIdAsync above which is only ever used for mutation.
    Task<LessonSlot?> GetDetailByIdAsync(int id, CancellationToken ct);

    // Read-only listing: AsNoTracking, real Skip/Take against the database.
    // Public/student-facing: only ever shows Available slots.
    Task<(IReadOnlyList<LessonSlot> Items, int TotalCount)> GetAvailablePagedAsync(
        int page, int pageSize, int? subjectId, int? tutorId, bool sortDescending, CancellationToken ct);

    // Read-only listing for a tutor's own management view: all statuses, own slots only.
    Task<(IReadOnlyList<LessonSlot> Items, int TotalCount)> GetByTutorPagedAsync(
        int tutorId, int page, int pageSize, CancellationToken ct);

    // Used by Subject deletion to enforce the "can't delete a subject in use" rule
    // as a clean business check instead of catching a raw FK-violation exception.
    Task<bool> AnyForSubjectAsync(int subjectId, CancellationToken ct);

    // A tutor can't teach two lessons at once: true if any of the tutor's
    // non-cancelled slots overlaps [startUtc, endUtc). excludeSlotId lets an
    // update ignore the slot being edited.
    Task<bool> HasOverlappingSlotAsync(
        int tutorId, DateTime startUtc, DateTime endUtc, int? excludeSlotId, CancellationToken ct);

    void Add(LessonSlot slot);

    void Remove(LessonSlot slot);
}
