using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using TutorScheduler.Core.Common;
using TutorScheduler.Core.DTOs.Auth;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Enums;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.Service;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IJwtTokenGenerator tokenGenerator,
        IPasswordHasher<User> passwordHasher,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _tokenGenerator = tokenGenerator;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<Result<AuthResponseDto>> RegisterAsync(
        string fullName, string email, string password, UserRole role, CancellationToken ct)
    {
        // Normalized so lookups are case-insensitive - Postgres `text` equality
        // is case-sensitive by default, and the Email index would otherwise
        // let "a@x.com" and "A@x.com" both register while only one can log in
        // by any given casing.
        email = email.Trim().ToLowerInvariant();

        // Only ever log email/role below - never password or the raw request DTO.
        if (await _userRepository.EmailExistsAsync(email, ct))
        {
            _logger.LogInformation("Registration rejected: email {Email} already exists", email);
            return Result<AuthResponseDto>.Conflict("An account with this email already exists.");
        }

        var user = new User
        {
            FullName = fullName,
            Email = email,
            Role = role,
            PasswordHash = string.Empty
        };

        // PasswordHasher needs the User instance for its salt/format, but never
        // reads PasswordHash from it here - only writes the result into it.
        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        _userRepository.Add(user);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("User {Email} registered with role {Role}", user.Email, user.Role);
        return Result<AuthResponseDto>.Success(BuildAuthResponse(user));
    }

    public async Task<Result<AuthResponseDto>> LoginAsync(string email, string password, CancellationToken ct)
    {
        email = email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email, ct);

        // Same generic message whether the email doesn't exist or the password
        // is wrong - distinguishing them would let an attacker enumerate
        // registered emails.
        if (user is null)
        {
            _logger.LogInformation("Login failed: no account for {Email}", email);
            return Result<AuthResponseDto>.Unauthorized("Invalid email or password.");
        }

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            _logger.LogInformation("Login failed: incorrect password for {Email}", email);
            return Result<AuthResponseDto>.Unauthorized("Invalid email or password.");
        }

        _logger.LogInformation("User {Email} logged in", user.Email);
        return Result<AuthResponseDto>.Success(BuildAuthResponse(user));
    }

    private AuthResponseDto BuildAuthResponse(User user)
    {
        var (token, expiresAtUtc) = _tokenGenerator.GenerateToken(user);

        return new AuthResponseDto
        {
            Token = token,
            ExpiresAtUtc = expiresAtUtc,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role
        };
    }
}
