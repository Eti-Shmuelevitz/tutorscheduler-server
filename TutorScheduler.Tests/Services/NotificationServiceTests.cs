using Moq;
using TutorScheduler.Core.Common;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Interfaces;
using TutorScheduler.Service;
using Xunit;

namespace TutorScheduler.Tests.Services;

public class NotificationServiceTests
{
    private const int UserId = 7;

    private readonly Mock<INotificationRepository> _repository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly NotificationService _sut;

    public NotificationServiceTests()
    {
        _sut = new NotificationService(_repository.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task MarkReadAsync_NotFound_ReturnsNotFound()
    {
        var result = await _sut.MarkReadAsync(UserId, 1, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task MarkReadAsync_SomeoneElsesNotification_ReturnsForbidden()
    {
        _repository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Notification { Id = 1, UserId = 99 });

        var result = await _sut.MarkReadAsync(UserId, 1, CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkReadAsync_Unread_SetsReadAt()
    {
        var notification = new Notification { Id = 1, UserId = UserId };
        _repository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(notification);

        var result = await _sut.MarkReadAsync(UserId, 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(notification.ReadAtUtc);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkReadAsync_AlreadyRead_IsANoOp()
    {
        var readAt = DateTime.UtcNow.AddDays(-1);
        var notification = new Notification { Id = 1, UserId = UserId, ReadAtUtc = readAt };
        _repository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(notification);

        var result = await _sut.MarkReadAsync(UserId, 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(readAt, notification.ReadAtUtc);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkAllReadAsync_MarksEveryUnreadNotification()
    {
        var unread = new List<Notification> { new() { Id = 1, UserId = UserId }, new() { Id = 2, UserId = UserId } };
        _repository.Setup(r => r.GetUnreadForUpdateAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(unread);

        var result = await _sut.MarkAllReadAsync(UserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.All(unread, n => Assert.NotNull(n.ReadAtUtc));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
