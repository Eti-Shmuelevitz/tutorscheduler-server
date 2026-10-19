using TutorScheduler.Core.Common;
using TutorScheduler.Core.Entities;

namespace TutorScheduler.Core.Interfaces;

public interface ISubjectService
{
    Task<IReadOnlyList<Subject>> GetAllAsync(CancellationToken ct);
    Task<Subject?> GetByIdAsync(int id, CancellationToken ct);
    Task<Result<Subject>> CreateAsync(string name, CancellationToken ct);
    Task<Result> UpdateAsync(int id, string name, CancellationToken ct);
    Task<Result> DeleteAsync(int id, CancellationToken ct);
}
