namespace TutorScheduler.API.Auth;

// Controllers depend on this abstraction, never on how identity is resolved -
// swapping HeaderCurrentUserService for a JWT-claims-based implementation
// later requires changing only that one class.
public interface ICurrentUserService
{
    int UserId { get; }
    string Role { get; }
}
