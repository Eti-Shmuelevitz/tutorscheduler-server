using System.ComponentModel.DataAnnotations;

namespace TutorScheduler.Core.DTOs.Subjects;

// Same shape for create and update - no reason to duplicate it.
public class SubjectRequestDto
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = default!;
}
