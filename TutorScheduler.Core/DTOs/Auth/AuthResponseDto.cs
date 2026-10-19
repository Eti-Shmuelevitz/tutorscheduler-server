using TutorScheduler.Core.Enums;

namespace TutorScheduler.Core.DTOs.Auth;

public class AuthResponseDto
{
    public string Token { get; set; } = default!;
    public DateTime ExpiresAtUtc { get; set; }
    public string FullName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public UserRole Role { get; set; }
}
