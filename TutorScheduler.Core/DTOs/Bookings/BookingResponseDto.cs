using TutorScheduler.Core.Enums;

namespace TutorScheduler.Core.DTOs.Bookings;

public class BookingResponseDto
{
    public int Id { get; set; }
    public int LessonSlotId { get; set; }
    public string SubjectName { get; set; } = default!;
    public string TutorName { get; set; } = default!;
    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }
    public DateTime BookedAtUtc { get; set; }
    public BookingStatus Status { get; set; }
}
