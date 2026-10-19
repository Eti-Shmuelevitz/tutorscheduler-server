using Moq;
using TutorScheduler.Core.Common;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Interfaces;
using TutorScheduler.Service;
using Xunit;

namespace TutorScheduler.Tests.Services;

public class SubjectServiceTests
{
    private readonly Mock<ISubjectRepository> _subjectRepository = new();
    private readonly Mock<ILessonSlotRepository> _lessonSlotRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly SubjectService _sut;

    public SubjectServiceTests()
    {
        _sut = new SubjectService(_subjectRepository.Object, _lessonSlotRepository.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task CreateAsync_BlankName_ReturnsValidationError()
    {
        var result = await _sut.CreateAsync("   ", CancellationToken.None);

        Assert.Equal(ResultStatus.ValidationError, result.Status);
        _subjectRepository.Verify(r => r.Add(It.IsAny<Subject>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ValidName_AddsSubject()
    {
        var result = await _sut.CreateAsync("Math", CancellationToken.None);

        Assert.True(result.IsSuccess);
        _subjectRepository.Verify(r => r.Add(It.Is<Subject>(s => s.Name == "Math")), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_SubjectNotFound_ReturnsNotFound()
    {
        _subjectRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((Subject?)null);

        var result = await _sut.DeleteAsync(1, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task DeleteAsync_SubjectInUse_ReturnsConflictWithoutRemoving()
    {
        _subjectRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subject { Id = 1, Name = "Math" });
        _lessonSlotRepository.Setup(r => r.AnyForSubjectAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _sut.DeleteAsync(1, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        _subjectRepository.Verify(r => r.Remove(It.IsAny<Subject>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_SubjectNotInUse_RemovesSubject()
    {
        var subject = new Subject { Id = 1, Name = "Math" };
        _subjectRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(subject);
        _lessonSlotRepository.Setup(r => r.AnyForSubjectAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _sut.DeleteAsync(1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _subjectRepository.Verify(r => r.Remove(subject), Times.Once);
    }
}
