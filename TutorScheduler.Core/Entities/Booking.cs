using TutorScheduler.Core.Enums;

namespace TutorScheduler.Core.Entities;

public class Booking
{
    public int Id { get; set; }

    public int LessonSlotId { get; set; }
    public LessonSlot LessonSlot { get; set; } = default!;

    public int StudentId { get; set; }
    public User Student { get; set; } = default!;

    public DateTime BookedAtUtc { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Active;
}
