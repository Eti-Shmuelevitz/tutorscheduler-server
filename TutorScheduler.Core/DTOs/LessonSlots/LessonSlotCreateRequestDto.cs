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

    // Nullable so [Required] actually rejects a missing price instead of
    // silently binding it to 0.
    [Required]
    [Range(typeof(decimal), "0", "10000", ErrorMessage = "Price must be between 0 and 10,000.")]
    public decimal? Price { get; set; }
}
