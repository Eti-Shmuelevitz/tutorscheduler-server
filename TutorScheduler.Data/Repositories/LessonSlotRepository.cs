using Microsoft.EntityFrameworkCore;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Enums;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.Data.Repositories;

public class LessonSlotRepository : ILessonSlotRepository
{
    private readonly AppDbContext _context;

    public LessonSlotRepository(AppDbContext context)
    {
        _context = context;
    }

    // Tracked on purpose: the booking flow mutates Status on this instance
    // and relies on the concurrency check running at SaveChangesAsync.
    public Task<LessonSlot?> GetByIdAsync(int id, CancellationToken ct) =>
        _context.LessonSlots
            .Include(s => s.Tutor)
            .Include(s => s.Subject)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<LessonSlot?> GetDetailByIdAsync(int id, CancellationToken ct) =>
        _context.LessonSlots
            .AsNoTracking()
            .Include(s => s.Tutor)
            .Include(s => s.Subject)
            .Include(s => s.Bookings)
                .ThenInclude(b => b.Student)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<(IReadOnlyList<LessonSlot> Items, int TotalCount)> GetAvailablePagedAsync(
        int page, int pageSize, int? subjectId, int? tutorId, bool sortDescending, CancellationToken ct)
    {
        // Slots that already started can't be booked (BookingService rejects
        // them), so listing them as "available" would only invite a 400.
        var now = DateTime.UtcNow;
        var query = _context.LessonSlots
            .AsNoTracking()
            .Where(s => s.Status == SlotStatus.Available && s.StartTimeUtc > now);

        if (subjectId.HasValue)
        {
            query = query.Where(s => s.SubjectId == subjectId.Value);
        }

        if (tutorId.HasValue)
        {
            query = query.Where(s => s.TutorId == tutorId.Value);
        }

        var totalCount = await query.CountAsync(ct);

        query = sortDescending
            ? query.OrderByDescending(s => s.StartTimeUtc)
            : query.OrderBy(s => s.StartTimeUtc);

        var items = await query
            .Include(s => s.Tutor)
            .Include(s => s.Subject)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<(IReadOnlyList<LessonSlot> Items, int TotalCount)> GetByTutorPagedAsync(
        int tutorId, int page, int pageSize, CancellationToken ct)
    {
        var query = _context.LessonSlots
            .AsNoTracking()
            .Where(s => s.TutorId == tutorId);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(s => s.Subject)
            .Include(s => s.Bookings)
                .ThenInclude(b => b.Student)
            .OrderByDescending(s => s.StartTimeUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<bool> AnyForSubjectAsync(int subjectId, CancellationToken ct) =>
        _context.LessonSlots.AsNoTracking().AnyAsync(s => s.SubjectId == subjectId, ct);

    // Same half-open overlap test as BookingRepository: touching slots
    // (10:00-11:00 and 11:00-12:00) do not overlap.
    public Task<bool> HasOverlappingSlotAsync(
        int tutorId, DateTime startUtc, DateTime endUtc, int? excludeSlotId, CancellationToken ct) =>
        _context.LessonSlots
            .AsNoTracking()
            .Where(s => s.TutorId == tutorId && s.Status != SlotStatus.Cancelled)
            .Where(s => excludeSlotId == null || s.Id != excludeSlotId)
            .AnyAsync(s => s.StartTimeUtc < endUtc && startUtc < s.EndTimeUtc, ct);

    public void Add(LessonSlot slot) => _context.LessonSlots.Add(slot);

    public void Remove(LessonSlot slot) => _context.LessonSlots.Remove(slot);
}
