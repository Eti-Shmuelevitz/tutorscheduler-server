using System.ComponentModel.DataAnnotations;

namespace TutorScheduler.Core.DTOs.LessonSlots;

public class LessonSlotCreateRequestDto
{
    [Required]
    public int SubjectId { get; set; }

    [Required]
    public DateTime StartTimeUtc { get; set; }

    [Required]
    public DateTime EndTimeUtc { get; set; }
}
