using TutorScheduler.Core.Enums;

namespace TutorScheduler.Core.DTOs.LessonSlots;

public class LessonSlotResponseDto
{
    public int Id { get; set; }
    public int TutorId { get; set; }
    public string TutorName { get; set; } = default!;
    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = default!;
    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }
    public SlotStatus Status { get; set; }

    // Null unless a student currently holds an active booking on this slot.
    public string? StudentName { get; set; }
}
