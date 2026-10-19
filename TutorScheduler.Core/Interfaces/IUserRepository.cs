using TutorScheduler.Core.Entities;

namespace TutorScheduler.Core.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id, CancellationToken ct);

    // Tracked: the booking / slot-creation flows rotate ScheduleVersion on the
    // returned entity so the concurrency check covers the user's whole schedule.
    Task<User?> GetForScheduleUpdateAsync(int id, CancellationToken ct);

    // Login and registration both need to look a user up by email.
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);

    Task<bool> EmailExistsAsync(string email, CancellationToken ct);

    void Add(User user);
}
