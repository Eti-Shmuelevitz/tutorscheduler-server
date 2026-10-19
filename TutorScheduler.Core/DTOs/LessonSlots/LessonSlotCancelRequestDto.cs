using System.ComponentModel.DataAnnotations;

namespace TutorScheduler.Core.DTOs.LessonSlots;

public class LessonSlotCancelRequestDto
{
    // Optional note shown to the student in their notification.
    [MaxLength(300)]
    public string? Reason { get; set; }
}
