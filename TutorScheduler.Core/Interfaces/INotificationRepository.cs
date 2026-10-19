using TutorScheduler.Core.Entities;

namespace TutorScheduler.Core.Interfaces;

public interface INotificationRepository
{
    // Read-only listing, newest first, with the lesson's subject and tutor.
    Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetByUserPagedAsync(
        int userId, int page, int pageSize, CancellationToken ct);

    Task<int> CountUnreadAsync(int userId, CancellationToken ct);

    // Tracked: used to mark notifications as read.
    Task<Notification?> GetByIdAsync(int id, CancellationToken ct);

    Task<IReadOnlyList<Notification>> GetUnreadForUpdateAsync(int userId, CancellationToken ct);

    void Add(Notification notification);
}
