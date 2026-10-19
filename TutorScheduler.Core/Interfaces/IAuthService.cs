using TutorScheduler.Core.Common;
using TutorScheduler.Core.DTOs.Auth;
using TutorScheduler.Core.Enums;

namespace TutorScheduler.Core.Interfaces;

public interface IAuthService
{
    Task<Result<AuthResponseDto>> RegisterAsync(string fullName, string email, string password, UserRole role, CancellationToken ct);
    Task<Result<AuthResponseDto>> LoginAsync(string email, string password, CancellationToken ct);
}
