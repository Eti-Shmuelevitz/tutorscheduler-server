using TutorScheduler.Core.Entities;

namespace TutorScheduler.Core.Interfaces;

public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAtUtc) GenerateToken(User user);
}
