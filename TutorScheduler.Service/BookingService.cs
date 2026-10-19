using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TutorScheduler.Core.Common;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Enums;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.Service;

public class BookingService : IBookingService
{
    private readonly ILessonSlotRepository _lessonSlotRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        ILessonSlotRepository lessonSlotRepository,
        IBookingRepository bookingRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        ILogger<BookingService> logger)
    {
        _lessonSlotRepository = lessonSlotRepository;
        _bookingRepository = bookingRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<Booking>> BookSlotAsync(int studentId, int lessonSlotId, CancellationToken ct)
    {
        _logger.LogDebug("Student {StudentId} attempting to book LessonSlot {LessonSlotId}", studentId, lessonSlotId);

        var slot = await _lessonSlotRepository.GetByIdAsync(lessonSlotId, ct);

        if (slot is null)
        {
            return Result<Booking>.NotFound("Lesson slot not found.");
        }

        // Business validation on current data state: belongs here, not in the Controller.
        if (slot.Status != SlotStatus.Available)
        {
            // Also a lost race - just one where the winner had already committed
            // by the time we read the row, so it's logged at the same level.
            _logger.LogWarning(
                "Booking rejected: LessonSlot {LessonSlotId} is {Status}, requested by Student {StudentId}",
                lessonSlotId, slot.Status, studentId);
            return Result<Booking>.Conflict("This lesson slot is no longer available.");
        }

        if (slot.StartTimeUtc <= DateTime.UtcNow)
        {
            return Result<Booking>.Fail("This lesson slot has already started.");
        }

        // Tracked: rotating its ScheduleVersion below is what makes the overlap
        // check race-safe (see User.ScheduleVersion).
        var student = await _userRepository.GetForScheduleUpdateAsync(studentId, ct);
        if (student is null)
        {
            return Result<Booking>.NotFound("Student not found.");
        }

        // A student can't be in two lessons at once. This is a distinct failure
        // from the Conflict above: that one is the race-for-the-last-seat case
        // this project is about, this one is a plain input-validation rule.
        var hasOverlap = await _bookingRepository.HasOverlappingActiveBookingAsync(
            studentId, slot.StartTimeUtc, slot.EndTimeUtc, ct);
        if (hasOverlap)
        {
            return Result<Booking>.Fail("This lesson slot overlaps with another lesson you already booked.");
        }

        // On its own the overlap check above is check-then-act: two parallel
        // requests for two *different* overlapping slots both pass it before
        // either commits. Rotating the student's ScheduleVersion makes both
        // requests write the same row, so only the first one can commit.
        student.ScheduleVersion = Guid.NewGuid();
        slot.Status = SlotStatus.Booked;

        var booking = new Booking
        {
            LessonSlotId = slot.Id,
            StudentId = studentId,
            BookedAtUtc = DateTime.UtcNow,
            Status = BookingStatus.Active
        };

        _bookingRepository.Add(booking);

        try
        {
            // Single SaveChangesAsync call: the LessonSlot update and the Booking
            // insert commit together, or neither does.
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Either the slot's Version or the student's ScheduleVersion was
            // stale: another student took this slot, or this same student
            // committed a different booking in parallel.
            _logger.LogWarning(
                "Booking conflict on LessonSlot {LessonSlotId} by Student {StudentId}",
                lessonSlotId, studentId);

            return Result<Booking>.Conflict(
                "This slot was just taken, or you booked another lesson at the same moment. Please refresh and try again.");
        }

        _logger.LogInformation(
            "Student {StudentId} booked LessonSlot {LessonSlotId} (Booking {BookingId})",
            studentId, lessonSlotId, booking.Id);
        return Result<Booking>.Success(booking);
    }

    public async Task<Result> CancelBookingAsync(int studentId, int bookingId, CancellationToken ct)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, ct);

        if (booking is null)
        {
            return Result.NotFound("Booking not found.");
        }

        if (booking.StudentId != studentId)
        {
            return Result.Forbidden("You can only cancel your own bookings.");
        }

        if (booking.Status != BookingStatus.Active)
        {
            return Result.Fail("This booking is already cancelled.");
        }

        booking.Status = BookingStatus.Cancelled;
        booking.LessonSlot.Status = SlotStatus.Available;

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Cancel conflict on Booking {BookingId}", bookingId);
            return Result.Conflict("This booking was already changed elsewhere. Please refresh and try again.");
        }

        return Result.Success();
    }

    public Task<(IReadOnlyList<Booking> Items, int TotalCount)> GetByStudentPagedAsync(
        int studentId, int page, int pageSize, CancellationToken ct) =>
        _bookingRepository.GetByStudentPagedAsync(studentId, page, pageSize, ct);

    public async Task<Booking?> GetByIdAsync(int studentId, int bookingId, CancellationToken ct)
    {
        var booking = await _bookingRepository.GetDetailByIdAsync(bookingId, ct);
        return booking is not null && booking.StudentId == studentId ? booking : null;
    }
}
