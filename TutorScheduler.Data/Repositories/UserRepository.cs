using Microsoft.EntityFrameworkCore;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.Data.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<User?> GetByIdAsync(int id, CancellationToken ct) =>
        _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    // Tracked on purpose: the caller rotates ScheduleVersion on this instance.
    public Task<User?> GetForScheduleUpdateAsync(int id, CancellationToken ct) =>
        _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct) =>
        _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct) =>
        _context.Users.AsNoTracking().AnyAsync(u => u.Email == email, ct);

    public void Add(User user) => _context.Users.Add(user);
}
