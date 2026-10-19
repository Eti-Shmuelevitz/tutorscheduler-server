using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using TutorScheduler.Core.Common;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Enums;
using TutorScheduler.Core.Interfaces;
using TutorScheduler.Service;
using Xunit;

namespace TutorScheduler.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IJwtTokenGenerator> _tokenGenerator = new();
    private readonly Mock<IPasswordHasher<User>> _passwordHasher = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _tokenGenerator.Setup(g => g.GenerateToken(It.IsAny<User>())).Returns(("token", DateTime.UtcNow.AddHours(1)));
        _passwordHasher.Setup(h => h.HashPassword(It.IsAny<User>(), It.IsAny<string>())).Returns("hash");

        _sut = new AuthService(
            _userRepository.Object,
            _unitOfWork.Object,
            _tokenGenerator.Object,
            _passwordHasher.Object,
            Mock.Of<ILogger<AuthService>>());
    }

    [Fact]
    public async Task RegisterAsync_EmailAlreadyExists_ReturnsConflict()
    {
        _userRepository.Setup(r => r.EmailExistsAsync("a@test.com", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _sut.RegisterAsync("A", "A@Test.com ", "Passw0rd!", UserRole.Student, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        _userRepository.Verify(r => r.Add(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_Valid_StoresHashNotPasswordAndReturnsToken()
    {
        var result = await _sut.RegisterAsync("A", "a@test.com", "Passw0rd!", UserRole.Tutor, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("token", result.Value!.Token);
        _userRepository.Verify(r => r.Add(It.Is<User>(u => u.PasswordHash == "hash" && u.Role == UserRole.Tutor)), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_UnknownEmail_ReturnsUnauthorized()
    {
        _userRepository.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _sut.LoginAsync("nobody@test.com", "x", CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsUnauthorizedWithSameMessageAsUnknownEmail()
    {
        var user = new User { Id = 1, Email = "a@test.com", PasswordHash = "hash", FullName = "A" };
        _userRepository.Setup(r => r.GetByEmailAsync("a@test.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.VerifyHashedPassword(user, "hash", "wrong")).Returns(PasswordVerificationResult.Failed);
        _userRepository.Setup(r => r.GetByEmailAsync("nobody@test.com", It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var wrongPassword = await _sut.LoginAsync("a@test.com", "wrong", CancellationToken.None);
        var unknownEmail = await _sut.LoginAsync("nobody@test.com", "wrong", CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, wrongPassword.Status);
        Assert.Equal(unknownEmail.Error, wrongPassword.Error);
    }

    [Fact]
    public async Task LoginAsync_CorrectPassword_ReturnsToken()
    {
        var user = new User { Id = 1, Email = "a@test.com", PasswordHash = "hash", FullName = "A", Role = UserRole.Student };
        _userRepository.Setup(r => r.GetByEmailAsync("a@test.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.VerifyHashedPassword(user, "hash", "Passw0rd!")).Returns(PasswordVerificationResult.Success);

        var result = await _sut.LoginAsync("A@test.com", "Passw0rd!", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserRole.Student, result.Value!.Role);
    }
}
