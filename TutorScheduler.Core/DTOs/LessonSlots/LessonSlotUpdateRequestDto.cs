using System.ComponentModel.DataAnnotations;

namespace TutorScheduler.Core.DTOs.LessonSlots;

// No SubjectId: the Service layer doesn't allow changing a slot's subject
// after creation, only its time window.
public class LessonSlotUpdateRequestDto
{
    [Required]
    public DateTime StartTimeUtc { get; set; }

    [Required]
    public DateTime EndTimeUtc { get; set; }
}
