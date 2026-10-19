using Microsoft.EntityFrameworkCore;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Enums;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.Data.Repositories;

public class BookingRepository : IBookingRepository
{
    private readonly AppDbContext _context;

    public BookingRepository(AppDbContext context)
    {
        _context = context;
    }

    // Tracked: the cancel flow mutates this Booking's Status and the
    // associated LessonSlot's Status in the same SaveChangesAsync call.
    public Task<Booking?> GetByIdAsync(int id, CancellationToken ct) =>
        _context.Bookings
            .Include(b => b.LessonSlot)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<(IReadOnlyList<Booking> Items, int TotalCount)> GetByStudentPagedAsync(
        int studentId, int page, int pageSize, CancellationToken ct)
    {
        var query = _context.Bookings
            .AsNoTracking()
            .Where(b => b.StudentId == studentId);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(b => b.LessonSlot)
                .ThenInclude(s => s.Subject)
            .Include(b => b.LessonSlot)
                .ThenInclude(s => s.Tutor)
            .OrderByDescending(b => b.BookedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    // AsNoTracking: for the Controller-facing single-booking read (detail view,
    // ownership check), never for a mutation flow.
    public Task<Booking?> GetDetailByIdAsync(int id, CancellationToken ct) =>
        _context.Bookings
            .AsNoTracking()
            .Include(b => b.LessonSlot)
                .ThenInclude(s => s.Subject)
            .Include(b => b.LessonSlot)
                .ThenInclude(s => s.Tutor)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<Booking?> GetActiveForSlotAsync(int lessonSlotId, CancellationToken ct) =>
        _context.Bookings
            .FirstOrDefaultAsync(b => b.LessonSlotId == lessonSlotId && b.Status == BookingStatus.Active, ct);

    public void Add(Booking booking) => _context.Bookings.Add(booking);

    public Task<bool> AnyForSlotAsync(int lessonSlotId, CancellationToken ct) =>
        _context.Bookings.AsNoTracking().AnyAsync(b => b.LessonSlotId == lessonSlotId, ct);

    // Standard half-open interval overlap test: two ranges overlap iff each
    // starts before the other ends.
    public Task<bool> HasOverlappingActiveBookingAsync(
        int studentId, DateTime startUtc, DateTime endUtc, CancellationToken ct) =>
        _context.Bookings
            .AsNoTracking()
            .Where(b => b.StudentId == studentId && b.Status == BookingStatus.Active)
            .AnyAsync(b => b.LessonSlot.StartTimeUtc < endUtc && startUtc < b.LessonSlot.EndTimeUtc, ct);
}
