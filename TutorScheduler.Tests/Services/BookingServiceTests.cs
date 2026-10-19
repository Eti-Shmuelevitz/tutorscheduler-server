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

public class BookingServiceTests
{
    private const int StudentId = 10;

    private readonly Mock<ILessonSlotRepository> _lessonSlotRepository = new();
    private readonly Mock<IBookingRepository> _bookingRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly User _student = new() { Id = StudentId, FullName = "Student", Email = "s@test.com", Role = UserRole.Student };
    private readonly BookingService _sut;

    public BookingServiceTests()
    {
        _userRepository.Setup(r => r.GetForScheduleUpdateAsync(StudentId, It.IsAny<CancellationToken>())).ReturnsAsync(_student);

        _sut = new BookingService(
            _lessonSlotRepository.Object,
            _bookingRepository.Object,
            _userRepository.Object,
            _unitOfWork.Object,
            Mock.Of<ILogger<BookingService>>());
    }

    private static LessonSlot FutureSlot(SlotStatus status = SlotStatus.Available) => new()
    {
        Id = 1,
        Status = status,
        StartTimeUtc = DateTime.UtcNow.AddDays(1),
        EndTimeUtc = DateTime.UtcNow.AddDays(1).AddHours(1)
    };

    private void GivenSlot(LessonSlot slot) =>
        _lessonSlotRepository.Setup(r => r.GetByIdAsync(slot.Id, It.IsAny<CancellationToken>())).ReturnsAsync(slot);

    [Fact]
    public async Task BookSlotAsync_SlotNotFound_ReturnsNotFound()
    {
        _lessonSlotRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LessonSlot?)null);

        var result = await _sut.BookSlotAsync(StudentId, lessonSlotId: 1, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        _bookingRepository.Verify(r => r.Add(It.IsAny<Booking>()), Times.Never);
    }

    // The limited-resource rule, rejected case: the slot is already taken.
    [Fact]
    public async Task BookSlotAsync_SlotAlreadyBooked_ReturnsConflictWithoutSaving()
    {
        GivenSlot(FutureSlot(SlotStatus.Booked));

        var result = await _sut.BookSlotAsync(StudentId, lessonSlotId: 1, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        _bookingRepository.Verify(r => r.Add(It.IsAny<Booking>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BookSlotAsync_SlotCancelledByTutor_ReturnsConflictWithoutSaving()
    {
        GivenSlot(FutureSlot(SlotStatus.Cancelled));

        var result = await _sut.BookSlotAsync(StudentId, lessonSlotId: 1, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // The limited-resource rule, happy path.
    [Fact]
    public async Task BookSlotAsync_SlotAvailable_BooksSuccessfully()
    {
        var slot = FutureSlot();
        GivenSlot(slot);

        var result = await _sut.BookSlotAsync(StudentId, lessonSlotId: 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SlotStatus.Booked, slot.Status);
        _bookingRepository.Verify(
            r => r.Add(It.Is<Booking>(b => b.StudentId == StudentId && b.LessonSlotId == 1 && b.Status == BookingStatus.Active)),
            Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Without this rotation, two parallel bookings of two different
    // overlapping slots by the same student would both commit.
    [Fact]
    public async Task BookSlotAsync_Success_RotatesStudentScheduleVersion()
    {
        GivenSlot(FutureSlot());
        var before = _student.ScheduleVersion;

        await _sut.BookSlotAsync(StudentId, lessonSlotId: 1, CancellationToken.None);

        Assert.NotEqual(before, _student.ScheduleVersion);
    }

    [Fact]
    public async Task BookSlotAsync_SlotAlreadyStarted_ReturnsValidationErrorWithoutSaving()
    {
        var slot = new LessonSlot
        {
            Id = 1,
            Status = SlotStatus.Available,
            StartTimeUtc = DateTime.UtcNow.AddMinutes(-5),
            EndTimeUtc = DateTime.UtcNow.AddMinutes(55)
        };
        GivenSlot(slot);

        var result = await _sut.BookSlotAsync(StudentId, lessonSlotId: 1, CancellationToken.None);

        Assert.Equal(ResultStatus.ValidationError, result.Status);
        Assert.Equal(SlotStatus.Available, slot.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BookSlotAsync_StudentNotFound_ReturnsNotFoundWithoutSaving()
    {
        GivenSlot(FutureSlot());

        var result = await _sut.BookSlotAsync(studentId: 999, lessonSlotId: 1, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BookSlotAsync_OverlapsExistingActiveBooking_ReturnsValidationErrorWithoutSaving()
    {
        var slot = FutureSlot();
        GivenSlot(slot);
        _bookingRepository
            .Setup(r => r.HasOverlappingActiveBookingAsync(StudentId, slot.StartTimeUtc, slot.EndTimeUtc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.BookSlotAsync(StudentId, lessonSlotId: 1, CancellationToken.None);

        Assert.Equal(ResultStatus.ValidationError, result.Status);
        Assert.Equal(SlotStatus.Available, slot.Status);
        _bookingRepository.Verify(r => r.Add(It.IsAny<Booking>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // The race lost at save time: another request committed between our read and our write.
    [Fact]
    public async Task BookSlotAsync_ConcurrencyExceptionOnSave_ReturnsConflict()
    {
        GivenSlot(FutureSlot());
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await _sut.BookSlotAsync(StudentId, lessonSlotId: 1, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task CancelBookingAsync_BookingNotFound_ReturnsNotFound()
    {
        _bookingRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((Booking?)null);

        var result = await _sut.CancelBookingAsync(StudentId, bookingId: 1, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task CancelBookingAsync_NotOwnBooking_ReturnsForbidden()
    {
        var slot = new LessonSlot { Id = 1, Status = SlotStatus.Booked };
        var booking = new Booking { Id = 1, StudentId = 99, LessonSlot = slot, Status = BookingStatus.Active };
        _bookingRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(booking);

        var result = await _sut.CancelBookingAsync(StudentId, bookingId: 1, CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(SlotStatus.Booked, slot.Status);
    }

    [Fact]
    public async Task CancelBookingAsync_AlreadyCancelled_ReturnsValidationError()
    {
        var slot = new LessonSlot { Id = 1, Status = SlotStatus.Available };
        var booking = new Booking { Id = 1, StudentId = StudentId, LessonSlot = slot, Status = BookingStatus.Cancelled };
        _bookingRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(booking);

        var result = await _sut.CancelBookingAsync(StudentId, bookingId: 1, CancellationToken.None);

        Assert.Equal(ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task CancelBookingAsync_ValidRequest_CancelsBookingAndReopensSlot()
    {
        var slot = new LessonSlot { Id = 1, Status = SlotStatus.Booked };
        var booking = new Booking { Id = 1, StudentId = StudentId, LessonSlot = slot, Status = BookingStatus.Active };
        _bookingRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(booking);

        var result = await _sut.CancelBookingAsync(StudentId, bookingId: 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(SlotStatus.Available, slot.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelBookingAsync_ConcurrencyExceptionOnSave_ReturnsConflict()
    {
        var slot = new LessonSlot { Id = 1, Status = SlotStatus.Booked };
        var booking = new Booking { Id = 1, StudentId = StudentId, LessonSlot = slot, Status = BookingStatus.Active };
        _bookingRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(booking);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await _sut.CancelBookingAsync(StudentId, bookingId: 1, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
    }
}
