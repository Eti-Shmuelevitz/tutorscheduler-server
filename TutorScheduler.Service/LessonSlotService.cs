using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TutorScheduler.Core.Common;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Enums;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.Service;

public class LessonSlotService : ILessonSlotService
{
    private readonly ILessonSlotRepository _lessonSlotRepository;
    private readonly ISubjectRepository _subjectRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IUserRepository _userRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LessonSlotService> _logger;

    public LessonSlotService(
        ILessonSlotRepository lessonSlotRepository,
        ISubjectRepository subjectRepository,
        IBookingRepository bookingRepository,
        IUserRepository userRepository,
        INotificationRepository notificationRepository,
        IUnitOfWork unitOfWork,
        ILogger<LessonSlotService> logger)
    {
        _lessonSlotRepository = lessonSlotRepository;
        _subjectRepository = subjectRepository;
        _bookingRepository = bookingRepository;
        _userRepository = userRepository;
        _notificationRepository = notificationRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    // Npgsql refuses to write a DateTime whose Kind isn't Utc to a timestamptz
    // column (it throws -> 500). A client that sends "2026-10-20T10:00:00"
    // without a trailing Z produces Kind=Unspecified, so normalize here: the
    // API contract is that every *Utc field is already UTC.
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public Task<LessonSlot?> GetByIdAsync(int id, CancellationToken ct) =>
        _lessonSlotRepository.GetDetailByIdAsync(id, ct);

    public Task<(IReadOnlyList<LessonSlot> Items, int TotalCount)> GetAvailablePagedAsync(
        int page, int pageSize, int? subjectId, int? tutorId, bool sortDescending, CancellationToken ct) =>
        _lessonSlotRepository.GetAvailablePagedAsync(page, pageSize, subjectId, tutorId, sortDescending, ct);

    public Task<(IReadOnlyList<LessonSlot> Items, int TotalCount)> GetByTutorPagedAsync(
        int tutorId, int page, int pageSize, CancellationToken ct) =>
        _lessonSlotRepository.GetByTutorPagedAsync(tutorId, page, pageSize, ct);

    public async Task<Result<LessonSlot>> CreateAsync(
        int tutorId, int subjectId, DateTime startUtc, DateTime endUtc, CancellationToken ct)
    {
        startUtc = ToUtc(startUtc);
        endUtc = ToUtc(endUtc);

        if (startUtc >= endUtc)
        {
            return Result<LessonSlot>.Fail("Start time must be before end time.");
        }

        if (startUtc < DateTime.UtcNow)
        {
            return Result<LessonSlot>.Fail("Cannot create a lesson slot in the past.");
        }

        var subject = await _subjectRepository.GetByIdAsync(subjectId, ct);
        if (subject is null)
        {
            return Result<LessonSlot>.NotFound("Subject not found.");
        }

        var tutor = await _userRepository.GetForScheduleUpdateAsync(tutorId, ct);
        if (tutor is null)
        {
            return Result<LessonSlot>.NotFound("Tutor not found.");
        }

        // A tutor can't teach two lessons at once - otherwise two students
        // could each legitimately book "their own" slot for the same hour.
        if (await _lessonSlotRepository.HasOverlappingSlotAsync(tutorId, startUtc, endUtc, null, ct))
        {
            return Result<LessonSlot>.Conflict("You already have a lesson slot that overlaps this time.");
        }

        // Same race as a student booking two overlapping slots in parallel
        // (see User.ScheduleVersion): makes a second, overlapping create that
        // slipped past the check above fail at save time instead of committing.
        tutor.ScheduleVersion = Guid.NewGuid();

        var slot = new LessonSlot
        {
            TutorId = tutorId,
            SubjectId = subjectId,
            StartTimeUtc = startUtc,
            EndTimeUtc = endUtc,
            Status = SlotStatus.Available
        };

        _lessonSlotRepository.Add(slot);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Create conflict: Tutor {TutorId} changed their schedule in parallel", tutorId);
            return Result<LessonSlot>.Conflict("Your schedule was changed at the same moment. Please refresh and try again.");
        }

        return Result<LessonSlot>.Success(slot);
    }

    public async Task<Result> UpdateAsync(int tutorId, int slotId, DateTime startUtc, DateTime endUtc, CancellationToken ct)
    {
        startUtc = ToUtc(startUtc);
        endUtc = ToUtc(endUtc);

        var slot = await _lessonSlotRepository.GetByIdAsync(slotId, ct);
        if (slot is null)
        {
            return Result.NotFound("Lesson slot not found.");
        }

        if (slot.TutorId != tutorId)
        {
            return Result.Forbidden("You can only edit your own lesson slots.");
        }

        // Booking history, not just current Status: a slot that was booked and
        // later cancelled is Available again, but its start/end time is now
        // part of a historical Booking record and must not be rewritten.
        if (await _bookingRepository.AnyForSlotAsync(slotId, ct))
        {
            return Result.Conflict("This slot has booking history and can no longer be edited.");
        }

        if (startUtc >= endUtc)
        {
            return Result.Fail("Start time must be before end time.");
        }

        if (startUtc < DateTime.UtcNow)
        {
            return Result.Fail("Cannot move a lesson slot into the past.");
        }

        var tutor = await _userRepository.GetForScheduleUpdateAsync(tutorId, ct);
        if (tutor is null)
        {
            return Result.NotFound("Tutor not found.");
        }

        if (await _lessonSlotRepository.HasOverlappingSlotAsync(tutorId, startUtc, endUtc, slotId, ct))
        {
            return Result.Conflict("You already have a lesson slot that overlaps this time.");
        }

        tutor.ScheduleVersion = Guid.NewGuid();
        slot.StartTimeUtc = startUtc;
        slot.EndTimeUtc = endUtc;

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Update conflict on LessonSlot {LessonSlotId} by Tutor {TutorId}", slotId, tutorId);
            return Result.Conflict("This slot was changed elsewhere. Please refresh and try again.");
        }

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int tutorId, int slotId, CancellationToken ct)
    {
        var slot = await _lessonSlotRepository.GetByIdAsync(slotId, ct);
        if (slot is null)
        {
            return Result.NotFound("Lesson slot not found.");
        }

        if (slot.TutorId != tutorId)
        {
            return Result.Forbidden("You can only delete your own lesson slots.");
        }

        if (slot.Status == SlotStatus.Booked)
        {
            return Result.Conflict("Cannot delete a slot with an active booking.");
        }

        if (await _bookingRepository.AnyForSlotAsync(slotId, ct))
        {
            // Soft delete: preserves the historical Booking row(s) instead of
            // letting a hard delete cascade over them.
            slot.Status = SlotStatus.Cancelled;
        }
        else
        {
            _lessonSlotRepository.Remove(slot);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Delete conflict on LessonSlot {LessonSlotId} by Tutor {TutorId}", slotId, tutorId);
            return Result.Conflict("This slot was changed elsewhere. Please refresh and try again.");
        }

        return Result.Success();
    }

    public async Task<Result> CancelByTutorAsync(int tutorId, int slotId, string? reason, CancellationToken ct)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        var slot = await _lessonSlotRepository.GetByIdAsync(slotId, ct);
        if (slot is null)
        {
            return Result.NotFound("Lesson slot not found.");
        }

        if (slot.TutorId != tutorId)
        {
            return Result.Forbidden("You can only cancel your own lesson slots.");
        }

        if (slot.Status == SlotStatus.Cancelled)
        {
            return Result.Fail("This lesson slot is already cancelled.");
        }

        if (slot.StartTimeUtc <= DateTime.UtcNow)
        {
            return Result.Fail("Cannot cancel a lesson that has already started.");
        }

        var booking = slot.Status == SlotStatus.Booked
            ? await _bookingRepository.GetActiveForSlotAsync(slotId, ct)
            : null;

        // Modifying the slot is what makes this race-safe: if a student books
        // (or cancels) it between our read and our save, the slot's Version no
        // longer matches and the whole save - slot, booking and notification -
        // is rejected instead of half-applying.
        slot.Status = SlotStatus.Cancelled;

        if (booking is not null)
        {
            booking.Status = BookingStatus.CancelledByTutor;
            _notificationRepository.Add(new Notification
            {
                UserId = booking.StudentId,
                Type = NotificationType.LessonCancelledByTutor,
                LessonSlotId = slot.Id,
                Reason = reason,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Tutor cancel conflict on LessonSlot {LessonSlotId} by Tutor {TutorId}", slotId, tutorId);
            return Result.Conflict("This lesson was changed at the same moment (a student may have just booked or cancelled it). Please refresh and try again.");
        }

        _logger.LogInformation(
            "Tutor {TutorId} cancelled LessonSlot {LessonSlotId}{Notified}",
            tutorId, slotId, booking is null ? string.Empty : $", notified Student {booking.StudentId}");
        return Result.Success();
    }
}
