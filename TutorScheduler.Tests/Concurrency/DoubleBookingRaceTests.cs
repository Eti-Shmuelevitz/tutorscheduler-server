using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TutorScheduler.Core.Common;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Enums;
using TutorScheduler.Core.Interfaces;
using TutorScheduler.Data;
using TutorScheduler.Data.Repositories;
using TutorScheduler.Service;
using Xunit;

namespace TutorScheduler.Tests.Concurrency;

// End-to-end races through the REAL services and repositories against a real
// relational engine (SQLite), with no mocks. Each "request" gets its own
// DbContext, exactly like two HTTP requests in production.
//
// A real race depends on thread timing, so a test that just fires two tasks
// would pass or fail by luck. Instead, PausingUnitOfWork stops request A right
// before it saves - after it has read the data and passed every business
// check - lets request B run to completion, and only then lets A save. That
// is the worst-case interleaving, reproduced deterministically on every run.
public class DoubleBookingRaceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly List<AppDbContext> _contexts = new();
    private readonly DateTime _tenOClock = DateTime.UtcNow.Date.AddDays(7).AddHours(10);

    public DoubleBookingRaceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var context = new AppDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }

        _connection.Dispose();
    }

    // ---------------------------------------------------------------- helpers

    private AppDbContext NewContext()
    {
        var context = new AppDbContext(_options);
        _contexts.Add(context);
        return context;
    }

    private async Task<int> AddUserAsync(string name, UserRole role)
    {
        using var context = new AppDbContext(_options);
        var user = new User { FullName = name, Email = $"{name}@test.com", PasswordHash = "x", Role = role };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> AddSubjectAsync()
    {
        using var context = new AppDbContext(_options);
        var subject = new Subject { Name = "Math" };
        context.Subjects.Add(subject);
        await context.SaveChangesAsync();
        return subject.Id;
    }

    private async Task<int> AddSlotAsync(int tutorId, int subjectId, DateTime start, int minutes = 60)
    {
        using var context = new AppDbContext(_options);
        var slot = new LessonSlot
        {
            TutorId = tutorId,
            SubjectId = subjectId,
            StartTimeUtc = start,
            EndTimeUtc = start.AddMinutes(minutes),
            Status = SlotStatus.Available
        };
        context.LessonSlots.Add(slot);
        await context.SaveChangesAsync();
        return slot.Id;
    }

    private BookingService NewBookingService(IUnitOfWork? unitOfWork = null)
    {
        var context = NewContext();
        return new BookingService(
            new LessonSlotRepository(context),
            new BookingRepository(context),
            new UserRepository(context),
            unitOfWork ?? new SqliteUnitOfWork(context),
            NullLogger<BookingService>.Instance);
    }

    private (BookingService Service, PausingUnitOfWork Gate) NewPausedBookingService()
    {
        var context = NewContext();
        var gate = new PausingUnitOfWork(new SqliteUnitOfWork(context));
        var service = new BookingService(
            new LessonSlotRepository(context),
            new BookingRepository(context),
            new UserRepository(context),
            gate,
            NullLogger<BookingService>.Instance);
        return (service, gate);
    }

    private LessonSlotService NewLessonSlotService(IUnitOfWork? unitOfWork = null, AppDbContext? context = null)
    {
        context ??= NewContext();
        return new LessonSlotService(
            new LessonSlotRepository(context),
            new SubjectRepository(context),
            new BookingRepository(context),
            new UserRepository(context),
            new NotificationRepository(context),
            unitOfWork ?? new SqliteUnitOfWork(context),
            NullLogger<LessonSlotService>.Instance);
    }

    // Runs `first` up to the moment it would save, runs `second` to completion,
    // then lets `first` save. Returns both results.
    private static async Task<(TFirst First, TSecond Second)> InterleaveAsync<TFirst, TSecond>(
        PausingUnitOfWork firstGate, Func<Task<TFirst>> first, Func<Task<TSecond>> second)
    {
        var firstTask = first();

        // A has read the row and passed all checks. Fail instead of hanging if
        // it returned early - then it never got far enough to race at all.
        var winner = await Task.WhenAny(firstGate.ReachedSave, firstTask);
        Assert.True(winner == firstGate.ReachedSave, "The first request finished before reaching SaveChanges.");

        var secondResult = await second(); // B runs and commits in the gap
        firstGate.Release();              // now A tries to save on stale data
        return (await firstTask, secondResult);
    }

    private List<Booking> ActiveBookings()
    {
        using var context = new AppDbContext(_options);
        return context.Bookings.AsNoTracking().Where(b => b.Status == BookingStatus.Active).ToList();
    }

    private SlotStatus SlotStatusOf(int slotId)
    {
        using var context = new AppDbContext(_options);
        return context.LessonSlots.AsNoTracking().Single(s => s.Id == slotId).Status;
    }

    // ------------------------------------------- two students, same slot

    [Fact]
    public async Task TwoStudents_SameSlot_SecondSaveOnStaleData_Gets409AndOnlyOneBookingExists()
    {
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var bob = await AddUserAsync("bob", UserRole.Student);
        var slot = await AddSlotAsync(tutor, await AddSubjectAsync(), _tenOClock);

        var (aliceService, aliceGate) = NewPausedBookingService();
        var bobService = NewBookingService();

        var (aliceResult, bobResult) = await InterleaveAsync(
            aliceGate,
            () => aliceService.BookSlotAsync(alice, slot, CancellationToken.None),
            () => bobService.BookSlotAsync(bob, slot, CancellationToken.None));

        Assert.True(bobResult.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, aliceResult.Status); // -> HTTP 409

        var active = Assert.Single(ActiveBookings());
        Assert.Equal(bob, active.StudentId);
        Assert.Equal(SlotStatus.Booked, SlotStatusOf(slot));
    }

    [Fact]
    public async Task SameStudent_SameSlot_DoubleClick_OnlyOneBookingExists()
    {
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var slot = await AddSlotAsync(tutor, await AddSubjectAsync(), _tenOClock);

        var (firstClick, gate) = NewPausedBookingService();
        var secondClick = NewBookingService();

        var (firstResult, secondResult) = await InterleaveAsync(
            gate,
            () => firstClick.BookSlotAsync(alice, slot, CancellationToken.None),
            () => secondClick.BookSlotAsync(alice, slot, CancellationToken.None));

        Assert.True(secondResult.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, firstResult.Status);
        Assert.Single(ActiveBookings());
    }

    [Fact]
    public async Task SlotAlreadyBooked_LateRequest_Gets409WithoutTouchingTheDatabase()
    {
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var bob = await AddUserAsync("bob", UserRole.Student);
        var slot = await AddSlotAsync(tutor, await AddSubjectAsync(), _tenOClock);

        Assert.True((await NewBookingService().BookSlotAsync(bob, slot, CancellationToken.None)).IsSuccess);
        var late = await NewBookingService().BookSlotAsync(alice, slot, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, late.Status);
        Assert.Equal(bob, Assert.Single(ActiveBookings()).StudentId);
    }

    // ------------------------- one student, two different overlapping slots

    // This is the race the per-slot Version token alone can NOT catch: the two
    // requests write two different LessonSlot rows. Before User.ScheduleVersion
    // existed, both of these bookings committed.
    [Fact]
    public async Task OneStudent_TwoOverlappingSlotsOfDifferentTutors_InParallel_OnlyOneBookingCommits()
    {
        var subject = await AddSubjectAsync();
        var tutorA = await AddUserAsync("tutorA", UserRole.Tutor);
        var tutorB = await AddUserAsync("tutorB", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var slot10To11 = await AddSlotAsync(tutorA, subject, _tenOClock);
        var slot1030To1130 = await AddSlotAsync(tutorB, subject, _tenOClock.AddMinutes(30));

        var (firstService, gate) = NewPausedBookingService();
        var secondService = NewBookingService();

        var (firstResult, secondResult) = await InterleaveAsync(
            gate,
            () => firstService.BookSlotAsync(alice, slot10To11, CancellationToken.None),
            () => secondService.BookSlotAsync(alice, slot1030To1130, CancellationToken.None));

        Assert.True(secondResult.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, firstResult.Status);

        var active = Assert.Single(ActiveBookings());
        Assert.Equal(slot1030To1130, active.LessonSlotId);

        // The losing request's whole transaction rolled back - its slot must
        // NOT be left marked Booked with no booking behind it.
        Assert.Equal(SlotStatus.Available, SlotStatusOf(slot10To11));
    }

    [Fact]
    public async Task OneStudent_OverlappingSlot_Sequential_IsRejected()
    {
        var subject = await AddSubjectAsync();
        var tutorA = await AddUserAsync("tutorA", UserRole.Tutor);
        var tutorB = await AddUserAsync("tutorB", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var slot10To11 = await AddSlotAsync(tutorA, subject, _tenOClock);
        var slot1059To1159 = await AddSlotAsync(tutorB, subject, _tenOClock.AddMinutes(59)); // overlaps by 1 minute

        var first = await NewBookingService().BookSlotAsync(alice, slot10To11, CancellationToken.None);
        var second = await NewBookingService().BookSlotAsync(alice, slot1059To1159, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(ResultStatus.ValidationError, second.Status);
        Assert.Single(ActiveBookings());
        Assert.Equal(SlotStatus.Available, SlotStatusOf(slot1059To1159));
    }

    [Fact]
    public async Task OneStudent_SlotFullyInsideAnotherBookedSlot_IsRejected()
    {
        var subject = await AddSubjectAsync();
        var tutorA = await AddUserAsync("tutorA", UserRole.Tutor);
        var tutorB = await AddUserAsync("tutorB", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var twoHourSlot = await AddSlotAsync(tutorA, subject, _tenOClock, minutes: 120);
        var innerSlot = await AddSlotAsync(tutorB, subject, _tenOClock.AddMinutes(30), minutes: 30);

        Assert.True((await NewBookingService().BookSlotAsync(alice, twoHourSlot, CancellationToken.None)).IsSuccess);
        var inner = await NewBookingService().BookSlotAsync(alice, innerSlot, CancellationToken.None);

        Assert.Equal(ResultStatus.ValidationError, inner.Status);
    }

    // Half-open intervals: 09-10, 10-11 and 11-12 touch but don't overlap.
    [Fact]
    public async Task OneStudent_BackToBackSlots_AreAllAllowed()
    {
        var subject = await AddSubjectAsync();
        var tutorA = await AddUserAsync("tutorA", UserRole.Tutor);
        var tutorB = await AddUserAsync("tutorB", UserRole.Tutor);
        var tutorC = await AddUserAsync("tutorC", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var nine = await AddSlotAsync(tutorA, subject, _tenOClock.AddHours(-1));
        var ten = await AddSlotAsync(tutorB, subject, _tenOClock);
        var eleven = await AddSlotAsync(tutorC, subject, _tenOClock.AddHours(1));

        Assert.True((await NewBookingService().BookSlotAsync(alice, ten, CancellationToken.None)).IsSuccess);
        Assert.True((await NewBookingService().BookSlotAsync(alice, nine, CancellationToken.None)).IsSuccess);
        Assert.True((await NewBookingService().BookSlotAsync(alice, eleven, CancellationToken.None)).IsSuccess);
        Assert.Equal(3, ActiveBookings().Count);
    }

    // A cancelled booking no longer blocks the student's time.
    [Fact]
    public async Task OneStudent_AfterCancelling_CanBookAnOverlappingSlot()
    {
        var subject = await AddSubjectAsync();
        var tutorA = await AddUserAsync("tutorA", UserRole.Tutor);
        var tutorB = await AddUserAsync("tutorB", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var slotA = await AddSlotAsync(tutorA, subject, _tenOClock);
        var slotB = await AddSlotAsync(tutorB, subject, _tenOClock.AddMinutes(30));

        var booked = await NewBookingService().BookSlotAsync(alice, slotA, CancellationToken.None);
        Assert.True((await NewBookingService().CancelBookingAsync(alice, booked.Value!.Id, CancellationToken.None)).IsSuccess);
        var rebooked = await NewBookingService().BookSlotAsync(alice, slotB, CancellationToken.None);

        Assert.True(rebooked.IsSuccess);
        Assert.Equal(slotB, Assert.Single(ActiveBookings()).LessonSlotId);
        Assert.Equal(SlotStatus.Available, SlotStatusOf(slotA));
    }

    // Two different students on two overlapping slots is fine - only the
    // *same* person can't be in two places at once.
    [Fact]
    public async Task TwoStudents_TwoOverlappingSlots_InParallel_BothSucceed()
    {
        var subject = await AddSubjectAsync();
        var tutorA = await AddUserAsync("tutorA", UserRole.Tutor);
        var tutorB = await AddUserAsync("tutorB", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var bob = await AddUserAsync("bob", UserRole.Student);
        var slotA = await AddSlotAsync(tutorA, subject, _tenOClock);
        var slotB = await AddSlotAsync(tutorB, subject, _tenOClock.AddMinutes(30));

        var (aliceService, gate) = NewPausedBookingService();
        var bobService = NewBookingService();

        var (aliceResult, bobResult) = await InterleaveAsync(
            gate,
            () => aliceService.BookSlotAsync(alice, slotA, CancellationToken.None),
            () => bobService.BookSlotAsync(bob, slotB, CancellationToken.None));

        Assert.True(aliceResult.IsSuccess);
        Assert.True(bobResult.IsSuccess);
        Assert.Equal(2, ActiveBookings().Count);
    }

    // ----------------------------------------- cancel / rebook races

    [Fact]
    public async Task CancelThenRebook_ByAnotherStudent_LeavesExactlyOneActiveBooking()
    {
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var bob = await AddUserAsync("bob", UserRole.Student);
        var slot = await AddSlotAsync(tutor, await AddSubjectAsync(), _tenOClock);

        var booking = await NewBookingService().BookSlotAsync(alice, slot, CancellationToken.None);
        await NewBookingService().CancelBookingAsync(alice, booking.Value!.Id, CancellationToken.None);
        var rebook = await NewBookingService().BookSlotAsync(bob, slot, CancellationToken.None);

        Assert.True(rebook.IsSuccess);
        Assert.Equal(bob, Assert.Single(ActiveBookings()).StudentId);
        Assert.Equal(SlotStatus.Booked, SlotStatusOf(slot));
    }

    // Student cancels while another student's booking attempt (made while the
    // slot was still Booked) is in flight - no state where both are "active".
    [Fact]
    public async Task TwoStudents_RaceToRebookAFreedSlot_OnlyOneWins()
    {
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var bob = await AddUserAsync("bob", UserRole.Student);
        var carol = await AddUserAsync("carol", UserRole.Student);
        var slot = await AddSlotAsync(tutor, await AddSubjectAsync(), _tenOClock);

        var booking = await NewBookingService().BookSlotAsync(alice, slot, CancellationToken.None);
        await NewBookingService().CancelBookingAsync(alice, booking.Value!.Id, CancellationToken.None);

        var (bobService, gate) = NewPausedBookingService();
        var carolService = NewBookingService();
        var (bobResult, carolResult) = await InterleaveAsync(
            gate,
            () => bobService.BookSlotAsync(bob, slot, CancellationToken.None),
            () => carolService.BookSlotAsync(carol, slot, CancellationToken.None));

        Assert.True(carolResult.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, bobResult.Status);
        Assert.Equal(carol, Assert.Single(ActiveBookings()).StudentId);
    }

    // ------------------------------------- tutor deletes while a student books

    [Fact]
    public async Task TutorDeletesSlot_WhileStudentIsBookingIt_StudentGets409AndNoOrphanBooking()
    {
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var slot = await AddSlotAsync(tutor, await AddSubjectAsync(), _tenOClock);

        var (aliceService, gate) = NewPausedBookingService();
        var tutorService = NewLessonSlotService();

        var (aliceResult, deleteResult) = await InterleaveAsync(
            gate,
            () => aliceService.BookSlotAsync(alice, slot, CancellationToken.None),
            () => tutorService.DeleteAsync(tutor, slot, CancellationToken.None));

        Assert.True(deleteResult.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, aliceResult.Status);
        Assert.Empty(ActiveBookings());
    }

    // ------------------------------------- tutor cancels a (booked) lesson

    private List<Notification> Notifications()
    {
        using var context = new AppDbContext(_options);
        return context.Notifications.AsNoTracking().ToList();
    }

    private BookingStatus BookingStatusOf(int bookingId)
    {
        using var context = new AppDbContext(_options);
        return context.Bookings.AsNoTracking().Single(b => b.Id == bookingId).Status;
    }

    [Fact]
    public async Task TutorCancelsBookedLesson_BookingCancelledByTutor_AndStudentNotified()
    {
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var slot = await AddSlotAsync(tutor, await AddSubjectAsync(), _tenOClock);
        var booking = await NewBookingService().BookSlotAsync(alice, slot, CancellationToken.None);

        var result = await NewLessonSlotService().CancelByTutorAsync(tutor, slot, "  Sick today  ", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SlotStatus.Cancelled, SlotStatusOf(slot));
        Assert.Equal(BookingStatus.CancelledByTutor, BookingStatusOf(booking.Value!.Id));
        Assert.Empty(ActiveBookings());
        var notification = Assert.Single(Notifications());
        Assert.Equal(alice, notification.UserId);
        Assert.Equal(slot, notification.LessonSlotId);
        Assert.Equal("Sick today", notification.Reason);
        Assert.Null(notification.ReadAtUtc);
    }

    [Fact]
    public async Task TutorCancelsUnbookedLesson_NoNotification()
    {
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var slot = await AddSlotAsync(tutor, await AddSubjectAsync(), _tenOClock);

        var result = await NewLessonSlotService().CancelByTutorAsync(tutor, slot, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SlotStatus.Cancelled, SlotStatusOf(slot));
        Assert.Empty(Notifications());
    }

    // Tutor read the slot as Available, then a student booked it before the
    // tutor saved: the tutor must get 409 - not cancel silently and leave the
    // student with an un-notified, dead booking.
    [Fact]
    public async Task TutorCancels_WhileStudentBooksFirst_TutorGets409_BookingKept()
    {
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var slot = await AddSlotAsync(tutor, await AddSubjectAsync(), _tenOClock);

        var tutorContext = NewContext();
        var gate = new PausingUnitOfWork(new SqliteUnitOfWork(tutorContext));
        var tutorService = NewLessonSlotService(gate, tutorContext);
        var aliceService = NewBookingService();

        var (cancelResult, bookResult) = await InterleaveAsync(
            gate,
            () => tutorService.CancelByTutorAsync(tutor, slot, null, CancellationToken.None),
            () => aliceService.BookSlotAsync(alice, slot, CancellationToken.None));

        Assert.True(bookResult.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, cancelResult.Status);
        Assert.Equal(SlotStatus.Booked, SlotStatusOf(slot));
        Assert.Single(ActiveBookings());
        Assert.Empty(Notifications());
    }

    // The other order: the tutor's cancel commits while the student's booking
    // is in flight. The student gets 409 and no orphan booking is left.
    [Fact]
    public async Task StudentBooks_WhileTutorCancelsFirst_StudentGets409()
    {
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var slot = await AddSlotAsync(tutor, await AddSubjectAsync(), _tenOClock);

        var (aliceService, gate) = NewPausedBookingService();
        var tutorService = NewLessonSlotService();

        var (bookResult, cancelResult) = await InterleaveAsync(
            gate,
            () => aliceService.BookSlotAsync(alice, slot, CancellationToken.None),
            () => tutorService.CancelByTutorAsync(tutor, slot, null, CancellationToken.None));

        Assert.True(cancelResult.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, bookResult.Status);
        Assert.Equal(SlotStatus.Cancelled, SlotStatusOf(slot));
        Assert.Empty(ActiveBookings());
    }

    // Student cancels their booking while the tutor's cancel (which saw it
    // booked) is in flight: the tutor gets 409, so the student is never told
    // "the tutor cancelled" about a booking they had already cancelled.
    [Fact]
    public async Task TutorCancels_WhileStudentCancelsFirst_TutorGets409_NoNotification()
    {
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var slot = await AddSlotAsync(tutor, await AddSubjectAsync(), _tenOClock);
        var booking = await NewBookingService().BookSlotAsync(alice, slot, CancellationToken.None);

        var tutorContext = NewContext();
        var gate = new PausingUnitOfWork(new SqliteUnitOfWork(tutorContext));
        var tutorService = NewLessonSlotService(gate, tutorContext);
        var aliceService = NewBookingService();

        var (cancelResult, studentCancel) = await InterleaveAsync(
            gate,
            () => tutorService.CancelByTutorAsync(tutor, slot, "Sorry", CancellationToken.None),
            () => aliceService.CancelBookingAsync(alice, booking.Value!.Id, CancellationToken.None));

        Assert.True(studentCancel.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, cancelResult.Status);
        Assert.Equal(BookingStatus.Cancelled, BookingStatusOf(booking.Value!.Id));
        Assert.Equal(SlotStatus.Available, SlotStatusOf(slot));
        Assert.Empty(Notifications());
    }

    [Fact]
    public async Task StudentCannotCancel_ABookingTheTutorAlreadyCancelled()
    {
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var slot = await AddSlotAsync(tutor, await AddSubjectAsync(), _tenOClock);
        var booking = await NewBookingService().BookSlotAsync(alice, slot, CancellationToken.None);
        await NewLessonSlotService().CancelByTutorAsync(tutor, slot, null, CancellationToken.None);

        var result = await NewBookingService().CancelBookingAsync(alice, booking.Value!.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.ValidationError, result.Status);
        Assert.Equal(SlotStatus.Cancelled, SlotStatusOf(slot)); // not flipped back to Available
    }

    // ---------------------------------------------- tutor overlapping slots

    [Fact]
    public async Task Tutor_CreatingOverlappingSlot_Sequential_IsRejected()
    {
        var subject = await AddSubjectAsync();
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);

        var first = await NewLessonSlotService().CreateAsync(tutor, subject, _tenOClock, _tenOClock.AddHours(1), price: 100m, CancellationToken.None);
        var overlapping = await NewLessonSlotService().CreateAsync(tutor, subject, _tenOClock.AddMinutes(30), _tenOClock.AddMinutes(90), price: 100m, CancellationToken.None);
        var adjacent = await NewLessonSlotService().CreateAsync(tutor, subject, _tenOClock.AddHours(1), _tenOClock.AddHours(2), price: 100m, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, overlapping.Status);
        Assert.True(adjacent.IsSuccess);
    }

    [Fact]
    public async Task Tutor_CreatingTwoOverlappingSlots_InParallel_OnlyOneIsCreated()
    {
        var subject = await AddSubjectAsync();
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);

        var firstContext = NewContext();
        var gate = new PausingUnitOfWork(new SqliteUnitOfWork(firstContext));
        var firstService = NewLessonSlotService(gate, firstContext);
        var secondService = NewLessonSlotService();

        var (firstResult, secondResult) = await InterleaveAsync(
            gate,
            () => firstService.CreateAsync(tutor, subject, _tenOClock, _tenOClock.AddHours(1), price: 100m, CancellationToken.None),
            () => secondService.CreateAsync(tutor, subject, _tenOClock.AddMinutes(15), _tenOClock.AddMinutes(75), price: 100m, CancellationToken.None));

        Assert.True(secondResult.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, firstResult.Status);

        using var verify = new AppDbContext(_options);
        Assert.Equal(1, verify.LessonSlots.Count(s => s.TutorId == tutor));
    }

    [Fact]
    public async Task Tutor_CancelledSlot_DoesNotBlockANewSlotAtTheSameTime()
    {
        var subject = await AddSubjectAsync();
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var slot = await AddSlotAsync(tutor, subject, _tenOClock);

        // Booking history forces a soft delete (Status = Cancelled).
        var booking = await NewBookingService().BookSlotAsync(alice, slot, CancellationToken.None);
        await NewBookingService().CancelBookingAsync(alice, booking.Value!.Id, CancellationToken.None);
        Assert.True((await NewLessonSlotService().DeleteAsync(tutor, slot, CancellationToken.None)).IsSuccess);
        Assert.Equal(SlotStatus.Cancelled, SlotStatusOf(slot));

        var replacement = await NewLessonSlotService().CreateAsync(tutor, subject, _tenOClock, _tenOClock.AddHours(1), price: 100m, CancellationToken.None);

        Assert.True(replacement.IsSuccess);
    }

    // ------------------------------------------- database-level safety net

    // Even a write that bypasses the Service layer entirely cannot create a
    // second active booking for one slot: the partial unique index rejects it.
    [Fact]
    public async Task Database_RejectsTwoActiveBookingsForOneSlot_EvenBypassingTheService()
    {
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var bob = await AddUserAsync("bob", UserRole.Student);
        var slot = await AddSlotAsync(tutor, await AddSubjectAsync(), _tenOClock);

        using var context = new AppDbContext(_options);
        context.Bookings.Add(new Booking { LessonSlotId = slot, StudentId = alice, BookedAtUtc = DateTime.UtcNow, Status = BookingStatus.Active });
        context.Bookings.Add(new Booking { LessonSlotId = slot, StudentId = bob, BookedAtUtc = DateTime.UtcNow, Status = BookingStatus.Active });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_AllowsManyCancelledBookingsPlusOneActive_ForOneSlot()
    {
        var tutor = await AddUserAsync("tutor", UserRole.Tutor);
        var alice = await AddUserAsync("alice", UserRole.Student);
        var bob = await AddUserAsync("bob", UserRole.Student);
        var slot = await AddSlotAsync(tutor, await AddSubjectAsync(), _tenOClock);

        using var context = new AppDbContext(_options);
        context.Bookings.Add(new Booking { LessonSlotId = slot, StudentId = alice, BookedAtUtc = DateTime.UtcNow, Status = BookingStatus.Cancelled });
        context.Bookings.Add(new Booking { LessonSlotId = slot, StudentId = bob, BookedAtUtc = DateTime.UtcNow, Status = BookingStatus.Cancelled });
        context.Bookings.Add(new Booking { LessonSlotId = slot, StudentId = alice, BookedAtUtc = DateTime.UtcNow, Status = BookingStatus.Active });

        await context.SaveChangesAsync();
    }

    // ------------------------------------------------------------------------

    // Wraps the real UnitOfWork and holds SaveChangesAsync until Release().
    private sealed class PausingUnitOfWork : IUnitOfWork
    {
        private readonly IUnitOfWork _inner;
        private readonly TaskCompletionSource _reachedSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PausingUnitOfWork(IUnitOfWork inner) => _inner = inner;

        public Task ReachedSave => _reachedSave.Task;

        public void Release() => _release.TrySetResult();

        public async Task<int> SaveChangesAsync(CancellationToken ct)
        {
            _reachedSave.TrySetResult();
            await _release.Task;
            return await _inner.SaveChangesAsync(ct);
        }
    }
}
