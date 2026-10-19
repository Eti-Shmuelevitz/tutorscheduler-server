using Microsoft.AspNetCore.Mvc;
using TutorScheduler.Core.DTOs.Auth;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.API.Controllers;

[Route("api/auth")]
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterRequestDto request, CancellationToken ct)
    {
        var result = await _authService.RegisterAsync(request.FullName, request.Email, request.Password, request.Role, ct);
        if (!result.IsSuccess)
        {
            return HandleFailure(result.Status, result.Error);
        }

        return Ok(result.Value);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequestDto request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request.Email, request.Password, ct);
        if (!result.IsSuccess)
        {
            return HandleFailure(result.Status, result.Error);
        }

        return Ok(result.Value);
    }
}
