using System.ComponentModel.DataAnnotations;
using TutorScheduler.Core.Enums;

namespace TutorScheduler.Core.DTOs.Auth;

public class RegisterRequestDto
{
    [Required]
    [MaxLength(100)]
    public string FullName { get; set; } = default!;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = default!;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = default!;

    [Required]
    public UserRole Role { get; set; }
}
