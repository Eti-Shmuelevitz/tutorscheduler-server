using Microsoft.EntityFrameworkCore;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.Data.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly AppDbContext _context;

    public NotificationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetByUserPagedAsync(
        int userId, int page, int pageSize, CancellationToken ct)
    {
        var query = _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(n => n.LessonSlot)
                .ThenInclude(s => s.Subject)
            .Include(n => n.LessonSlot)
                .ThenInclude(s => s.Tutor)
            .OrderByDescending(n => n.CreatedAtUtc)
            .ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<int> CountUnreadAsync(int userId, CancellationToken ct) =>
        _context.Notifications.AsNoTracking().CountAsync(n => n.UserId == userId && n.ReadAtUtc == null, ct);

    public Task<Notification?> GetByIdAsync(int id, CancellationToken ct) =>
        _context.Notifications.FirstOrDefaultAsync(n => n.Id == id, ct);

    public async Task<IReadOnlyList<Notification>> GetUnreadForUpdateAsync(int userId, CancellationToken ct) =>
        await _context.Notifications
            .Where(n => n.UserId == userId && n.ReadAtUtc == null)
            .ToListAsync(ct);

    public void Add(Notification notification) => _context.Notifications.Add(notification);
}
