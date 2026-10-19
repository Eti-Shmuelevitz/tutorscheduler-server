using TutorScheduler.Core.Common;
using TutorScheduler.Core.Entities;

namespace TutorScheduler.Core.Interfaces;

public interface ILessonSlotService
{
    Task<LessonSlot?> GetByIdAsync(int id, CancellationToken ct);

    Task<(IReadOnlyList<LessonSlot> Items, int TotalCount)> GetAvailablePagedAsync(
        int page, int pageSize, int? subjectId, int? tutorId, bool sortDescending, CancellationToken ct);

    Task<(IReadOnlyList<LessonSlot> Items, int TotalCount)> GetByTutorPagedAsync(
        int tutorId, int page, int pageSize, CancellationToken ct);

    Task<Result<LessonSlot>> CreateAsync(int tutorId, int subjectId, DateTime startUtc, DateTime endUtc, CancellationToken ct);

    // Rejected once the slot has any booking history, not just while Active -
    // editing a slot after a cancelled booking still rewrites what the student saw.
    Task<Result> UpdateAsync(int tutorId, int slotId, DateTime startUtc, DateTime endUtc, CancellationToken ct);

    Task<Result> DeleteAsync(int tutorId, int slotId, CancellationToken ct);

    // Cancels a future slot even when it's booked: the booking becomes
    // CancelledByTutor and the student gets a Notification, all in one save.
    Task<Result> CancelByTutorAsync(int tutorId, int slotId, string? reason, CancellationToken ct);
}
