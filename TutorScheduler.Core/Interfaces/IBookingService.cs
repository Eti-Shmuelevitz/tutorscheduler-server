using TutorScheduler.Core.Common;
using TutorScheduler.Core.Entities;

namespace TutorScheduler.Core.Interfaces;

public interface IBookingService
{
    // The contested operation: only one caller can win when two students
    // target the same LessonSlot at the same time.
    Task<Result<Booking>> BookSlotAsync(int studentId, int lessonSlotId, CancellationToken ct);

    Task<Result> CancelBookingAsync(int studentId, int bookingId, CancellationToken ct);

    Task<(IReadOnlyList<Booking> Items, int TotalCount)> GetByStudentPagedAsync(
        int studentId, int page, int pageSize, CancellationToken ct);

    // Returns null if the booking doesn't exist OR doesn't belong to this
    // student - ownership is enforced here, not left to the Controller.
    Task<Booking?> GetByIdAsync(int studentId, int bookingId, CancellationToken ct);
}
