using TutorScheduler.Core.Common;
using TutorScheduler.Core.Entities;

namespace TutorScheduler.Core.Interfaces;

public interface INotificationService
{
    Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetMinePagedAsync(
        int userId, int page, int pageSize, CancellationToken ct);

    Task<int> GetUnreadCountAsync(int userId, CancellationToken ct);

    Task<Result> MarkReadAsync(int userId, int notificationId, CancellationToken ct);

    Task<Result> MarkAllReadAsync(int userId, CancellationToken ct);
}
