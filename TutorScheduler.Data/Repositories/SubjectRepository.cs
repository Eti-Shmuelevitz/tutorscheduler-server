using Microsoft.EntityFrameworkCore;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.Data.Repositories;

public class SubjectRepository : ISubjectRepository
{
    private readonly AppDbContext _context;

    public SubjectRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Subject>> GetAllAsync(CancellationToken ct) =>
        await _context.Subjects.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);

    // Tracked (no AsNoTracking): matches the convention on LessonSlot/Booking -
    // a single-entity fetch by id is for the caller to inspect or mutate.
    public Task<Subject?> GetByIdAsync(int id, CancellationToken ct) =>
        _context.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<Subject?> GetDetailByIdAsync(int id, CancellationToken ct) =>
        _context.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);

    public void Add(Subject subject) => _context.Subjects.Add(subject);

    public void Remove(Subject subject) => _context.Subjects.Remove(subject);
}
