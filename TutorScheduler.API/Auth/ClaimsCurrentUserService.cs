using System.Security.Claims;

namespace TutorScheduler.API.Auth;

// Real implementation: identity comes from the validated JWT's claims, set by
// the [Authorize]-gated authentication middleware before the action runs.
public class ClaimsCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClaimsCurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(value, out var userId))
            {
                // Should be unreachable on any [Authorize]-protected action:
                // the middleware rejects the request before this ever runs.
                throw new InvalidOperationException("No authenticated user id claim found.");
            }

            return userId;
        }
    }

    public string Role
    {
        get
        {
            var role = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role);
            if (string.IsNullOrEmpty(role))
            {
                throw new InvalidOperationException("No authenticated user role claim found.");
            }

            return role;
        }
    }
}
