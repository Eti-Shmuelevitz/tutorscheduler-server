using TutorScheduler.Core.Common;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.Service;

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IUnitOfWork _unitOfWork;

    public NotificationService(INotificationRepository notificationRepository, IUnitOfWork unitOfWork)
    {
        _notificationRepository = notificationRepository;
        _unitOfWork = unitOfWork;
    }

    public Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetMinePagedAsync(
        int userId, int page, int pageSize, CancellationToken ct) =>
        _notificationRepository.GetByUserPagedAsync(userId, page, pageSize, ct);

    public Task<int> GetUnreadCountAsync(int userId, CancellationToken ct) =>
        _notificationRepository.CountUnreadAsync(userId, ct);

    public async Task<Result> MarkReadAsync(int userId, int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return Result.NotFound("Notification not found.");
        }

        if (notification.UserId != userId)
        {
            return Result.Forbidden("You can only read your own notifications.");
        }

        // Idempotent: marking an already-read notification again is a no-op.
        if (notification.ReadAtUtc is null)
        {
            notification.ReadAtUtc = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return Result.Success();
    }

    public async Task<Result> MarkAllReadAsync(int userId, CancellationToken ct)
    {
        var unread = await _notificationRepository.GetUnreadForUpdateAsync(userId, ct);
        if (unread.Count == 0)
        {
            return Result.Success();
        }

        var now = DateTime.UtcNow;
        foreach (var notification in unread)
        {
            notification.ReadAtUtc = now;
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
