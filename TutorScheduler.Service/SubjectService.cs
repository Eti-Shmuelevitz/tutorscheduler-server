using TutorScheduler.Core.Common;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.Service;

public class SubjectService : ISubjectService
{
    private readonly ISubjectRepository _subjectRepository;
    private readonly ILessonSlotRepository _lessonSlotRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SubjectService(
        ISubjectRepository subjectRepository,
        ILessonSlotRepository lessonSlotRepository,
        IUnitOfWork unitOfWork)
    {
        _subjectRepository = subjectRepository;
        _lessonSlotRepository = lessonSlotRepository;
        _unitOfWork = unitOfWork;
    }

    public Task<IReadOnlyList<Subject>> GetAllAsync(CancellationToken ct) =>
        _subjectRepository.GetAllAsync(ct);

    public Task<Subject?> GetByIdAsync(int id, CancellationToken ct) =>
        _subjectRepository.GetDetailByIdAsync(id, ct);

    public async Task<Result<Subject>> CreateAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Subject>.Fail("Subject name is required.");
        }

        var subject = new Subject { Name = name.Trim() };
        _subjectRepository.Add(subject);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<Subject>.Success(subject);
    }

    public async Task<Result> UpdateAsync(int id, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Fail("Subject name is required.");
        }

        var subject = await _subjectRepository.GetByIdAsync(id, ct);
        if (subject is null)
        {
            return Result.NotFound("Subject not found.");
        }

        subject.Name = name.Trim();
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct)
    {
        var subject = await _subjectRepository.GetByIdAsync(id, ct);
        if (subject is null)
        {
            return Result.NotFound("Subject not found.");
        }

        if (await _lessonSlotRepository.AnyForSubjectAsync(id, ct))
        {
            return Result.Conflict("Cannot delete a subject that has lesson slots.");
        }

        _subjectRepository.Remove(subject);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
