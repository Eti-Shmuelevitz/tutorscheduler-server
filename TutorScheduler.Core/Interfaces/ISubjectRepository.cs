using TutorScheduler.Core.Entities;

namespace TutorScheduler.Core.Interfaces;

public interface ISubjectRepository
{
    // Read-only listing: AsNoTracking. Small, unpaginated reference list.
    Task<IReadOnlyList<Subject>> GetAllAsync(CancellationToken ct);

    // Tracked: single-entity fetch for mutation (Update/Delete flows).
    Task<Subject?> GetByIdAsync(int id, CancellationToken ct);

    // AsNoTracking: for Controller-facing detail reads.
    Task<Subject?> GetDetailByIdAsync(int id, CancellationToken ct);

    void Add(Subject subject);

    void Remove(Subject subject);
}
