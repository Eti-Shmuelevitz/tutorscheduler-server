using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TutorScheduler.Core.Common;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Enums;
using TutorScheduler.Core.Interfaces;
using TutorScheduler.Service;
using Xunit;

namespace TutorScheduler.Tests.Services;

public class LessonSlotServiceTests
{
    private const int TutorId = 1;

    private readonly Mock<ILessonSlotRepository> _lessonSlotRepository = new();
    private readonly Mock<ISubjectRepository> _subjectRepository = new();
    private readonly Mock<IBookingRepository> _bookingRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<INotificationRepository> _notificationRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly User _tutor = new() { Id = TutorId, FullName = "Tutor", Email = "t@test.com", Role = UserRole.Tutor };
    private readonly LessonSlotService _sut;

    public LessonSlotServiceTests()
    {
        _userRepository.Setup(r => r.GetForScheduleUpdateAsync(TutorId, It.IsAny<CancellationToken>())).ReturnsAsync(_tutor);
        _subjectRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subject { Id = 1, Name = "Math" });

        _sut = new LessonSlotService(
            _lessonSlotRepository.Object,
            _subjectRepository.Object,
            _bookingRepository.Object,
            _userRepository.Object,
            _notificationRepository.Object,
            _unitOfWork.Object,
            Mock.Of<ILogger<LessonSlotService>>());
    }

    private void GivenOwnSlot(LessonSlot slot) =>
        _lessonSlotRepository.Setup(r => r.GetByIdAsync(slot.Id, It.IsAny<CancellationToken>())).ReturnsAsync(slot);

    private void GivenTutorHasOverlap() =>
        _lessonSlotRepository
            .Setup(r => r.HasOverlappingSlotAsync(TutorId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

    [Fact]
    public async Task CreateAsync_StartAfterEnd_ReturnsValidationError()
    {
        var result = await _sut.CreateAsync(
            TutorId, subjectId: 1, DateTime.UtcNow.AddHours(2), DateTime.UtcNow.AddHours(1), price: 100m, CancellationToken.None);

        Assert.Equal(ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task CreateAsync_StartEqualsEnd_ReturnsValidationError()
    {
        var at = DateTime.UtcNow.AddHours(2);

        var result = await _sut.CreateAsync(TutorId, subjectId: 1, at, at, price: 100m, CancellationToken.None);

        Assert.Equal(ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task CreateAsync_StartInPast_ReturnsValidationError()
    {
        var result = await _sut.CreateAsync(
            TutorId, subjectId: 1, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1), price: 100m, CancellationToken.None);

        Assert.Equal(ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task CreateAsync_SubjectNotFound_ReturnsNotFound()
    {
        var result = await _sut.CreateAsync(
            TutorId, subjectId: 42, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2), price: 100m, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task CreateAsync_Valid_CreatesAvailableSlotAndRotatesTutorScheduleVersion()
    {
        var before = _tutor.ScheduleVersion;

        var result = await _sut.CreateAsync(
            TutorId, subjectId: 1, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2), price: 100m, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SlotStatus.Available, result.Value!.Status);
        Assert.NotEqual(before, _tutor.ScheduleVersion);
        _lessonSlotRepository.Verify(r => r.Add(It.Is<LessonSlot>(s => s.TutorId == TutorId && s.SubjectId == 1)), Times.Once);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_000.01)]
    public async Task CreateAsync_PriceOutOfRange_ReturnsValidationError(decimal price)
    {
        var result = await _sut.CreateAsync(
            TutorId, subjectId: 1, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2), price, CancellationToken.None);

        Assert.Equal(ResultStatus.ValidationError, result.Status);
        _lessonSlotRepository.Verify(r => r.Add(It.IsAny<LessonSlot>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_Valid_StoresPriceRoundedToAgorot()
    {
        var result = await _sut.CreateAsync(
            TutorId, subjectId: 1, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2), 149.999m, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(150.00m, result.Value!.Price);
    }

    // A tutor can't teach two lessons at once.
    [Fact]
    public async Task CreateAsync_OverlapsTutorsExistingSlot_ReturnsConflictWithoutAdding()
    {
        GivenTutorHasOverlap();

        var result = await _sut.CreateAsync(
            TutorId, subjectId: 1, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2), price: 100m, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        _lessonSlotRepository.Verify(r => r.Add(It.IsAny<LessonSlot>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ConcurrencyExceptionOnSave_ReturnsConflict()
    {
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await _sut.CreateAsync(
            TutorId, subjectId: 1, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2), price: 100m, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
    }

    // Npgsql throws on a non-UTC DateTime; a timestamp sent without "Z" must not become a 500.
    [Fact]
    public async Task CreateAsync_UnspecifiedKindTimes_AreStoredAsUtc()
    {
        var start = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(1), DateTimeKind.Unspecified);
        var end = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(2), DateTimeKind.Unspecified);

        var result = await _sut.CreateAsync(TutorId, subjectId: 1, start, end, price: 100m, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeKind.Utc, result.Value!.StartTimeUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, result.Value!.EndTimeUtc.Kind);
    }

    [Fact]
    public async Task UpdateAsync_NotOwnSlot_ReturnsForbidden()
    {
        GivenOwnSlot(new LessonSlot { Id = 1, TutorId = 99, Status = SlotStatus.Available });

        var result = await _sut.UpdateAsync(
            TutorId, slotId: 1, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2), CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task UpdateAsync_HasBookingHistory_ReturnsConflict()
    {
        GivenOwnSlot(new LessonSlot { Id = 1, TutorId = TutorId, Status = SlotStatus.Available });
        _bookingRepository.Setup(r => r.AnyForSlotAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _sut.UpdateAsync(
            TutorId, slotId: 1, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task UpdateAsync_MovedOntoAnotherOwnSlot_ReturnsConflictAndLeavesTimesUnchanged()
    {
        var originalStart = DateTime.UtcNow.AddDays(1);
        var slot = new LessonSlot { Id = 1, TutorId = TutorId, Status = SlotStatus.Available, StartTimeUtc = originalStart, EndTimeUtc = originalStart.AddHours(1) };
        GivenOwnSlot(slot);
        GivenTutorHasOverlap();

        var result = await _sut.UpdateAsync(
            TutorId, slotId: 1, DateTime.UtcNow.AddHours(3), DateTime.UtcNow.AddHours(4), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(originalStart, slot.StartTimeUtc);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // The slot being edited must not count as overlapping with itself.
    [Fact]
    public async Task UpdateAsync_OverlapCheck_ExcludesTheSlotBeingEdited()
    {
        GivenOwnSlot(new LessonSlot { Id = 7, TutorId = TutorId, Status = SlotStatus.Available });

        await _sut.UpdateAsync(TutorId, slotId: 7, DateTime.UtcNow.AddHours(3), DateTime.UtcNow.AddHours(4), CancellationToken.None);

        _lessonSlotRepository.Verify(r => r.HasOverlappingSlotAsync(
            TutorId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), 7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_Valid_UpdatesTimeWindow()
    {
        var slot = new LessonSlot { Id = 1, TutorId = TutorId, Status = SlotStatus.Available };
        GivenOwnSlot(slot);
        var newStart = DateTime.UtcNow.AddHours(3);
        var newEnd = DateTime.UtcNow.AddHours(4);

        var result = await _sut.UpdateAsync(TutorId, slotId: 1, newStart, newEnd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newStart, slot.StartTimeUtc);
        Assert.Equal(newEnd, slot.EndTimeUtc);
    }

    [Fact]
    public async Task UpdateAsync_ConcurrencyExceptionOnSave_ReturnsConflict()
    {
        GivenOwnSlot(new LessonSlot { Id = 1, TutorId = TutorId, Status = SlotStatus.Available });
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await _sut.UpdateAsync(
            TutorId, slotId: 1, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task DeleteAsync_NotOwnSlot_ReturnsForbidden()
    {
        GivenOwnSlot(new LessonSlot { Id = 1, TutorId = 99, Status = SlotStatus.Available });

        var result = await _sut.DeleteAsync(TutorId, slotId: 1, CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        _lessonSlotRepository.Verify(r => r.Remove(It.IsAny<LessonSlot>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_BookedSlot_ReturnsConflictWithoutRemoving()
    {
        GivenOwnSlot(new LessonSlot { Id = 1, TutorId = TutorId, Status = SlotStatus.Booked });

        var result = await _sut.DeleteAsync(TutorId, slotId: 1, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        _lessonSlotRepository.Verify(r => r.Remove(It.IsAny<LessonSlot>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_NoBookingHistory_HardDeletes()
    {
        var slot = new LessonSlot { Id = 1, TutorId = TutorId, Status = SlotStatus.Available };
        GivenOwnSlot(slot);
        _bookingRepository.Setup(r => r.AnyForSlotAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _sut.DeleteAsync(TutorId, slotId: 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _lessonSlotRepository.Verify(r => r.Remove(slot), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_HasBookingHistory_SoftDeletesInsteadOfRemoving()
    {
        // Regression test for the cascade-delete bug caught during design:
        // hard-deleting a slot with booking history would wipe those Booking
        // rows via the FK cascade, destroying the audit trail.
        var slot = new LessonSlot { Id = 1, TutorId = TutorId, Status = SlotStatus.Available };
        GivenOwnSlot(slot);
        _bookingRepository.Setup(r => r.AnyForSlotAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _sut.DeleteAsync(TutorId, slotId: 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SlotStatus.Cancelled, slot.Status);
        _lessonSlotRepository.Verify(r => r.Remove(It.IsAny<LessonSlot>()), Times.Never);
    }

    // ---------------------------------------------------- CancelByTutorAsync

    private static LessonSlot FutureSlot(SlotStatus status, int tutorId = TutorId) => new()
    {
        Id = 1,
        TutorId = tutorId,
        Status = status,
        StartTimeUtc = DateTime.UtcNow.AddDays(1),
        EndTimeUtc = DateTime.UtcNow.AddDays(1).AddHours(1)
    };

    [Fact]
    public async Task CancelByTutorAsync_SlotNotFound_ReturnsNotFound()
    {
        var result = await _sut.CancelByTutorAsync(TutorId, slotId: 99, null, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task CancelByTutorAsync_AnotherTutorsSlot_ReturnsForbidden()
    {
        GivenOwnSlot(FutureSlot(SlotStatus.Booked, tutorId: 2));

        var result = await _sut.CancelByTutorAsync(TutorId, slotId: 1, null, CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelByTutorAsync_AlreadyCancelled_ReturnsValidationError()
    {
        GivenOwnSlot(FutureSlot(SlotStatus.Cancelled));

        var result = await _sut.CancelByTutorAsync(TutorId, slotId: 1, null, CancellationToken.None);

        Assert.Equal(ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task CancelByTutorAsync_LessonAlreadyStarted_ReturnsValidationError()
    {
        var slot = FutureSlot(SlotStatus.Booked);
        slot.StartTimeUtc = DateTime.UtcNow.AddMinutes(-5);
        GivenOwnSlot(slot);

        var result = await _sut.CancelByTutorAsync(TutorId, slotId: 1, null, CancellationToken.None);

        Assert.Equal(ResultStatus.ValidationError, result.Status);
        Assert.Equal(SlotStatus.Booked, slot.Status);
    }

    [Fact]
    public async Task CancelByTutorAsync_Booked_CancelsBookingAndNotifiesStudent()
    {
        var slot = FutureSlot(SlotStatus.Booked);
        var booking = new Booking { Id = 5, LessonSlotId = 1, StudentId = 7, Status = BookingStatus.Active };
        GivenOwnSlot(slot);
        _bookingRepository.Setup(r => r.GetActiveForSlotAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(booking);
        Notification? added = null;
        _notificationRepository.Setup(r => r.Add(It.IsAny<Notification>())).Callback<Notification>(n => added = n);

        var result = await _sut.CancelByTutorAsync(TutorId, slotId: 1, "   ", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SlotStatus.Cancelled, slot.Status);
        Assert.Equal(BookingStatus.CancelledByTutor, booking.Status);
        Assert.NotNull(added);
        Assert.Equal(7, added!.UserId);
        Assert.Equal(NotificationType.LessonCancelledByTutor, added.Type);
        Assert.Null(added.Reason); // whitespace-only reason is dropped
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelByTutorAsync_ConcurrentChange_ReturnsConflict()
    {
        GivenOwnSlot(FutureSlot(SlotStatus.Available));
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await _sut.CancelByTutorAsync(TutorId, slotId: 1, null, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
    }
}
