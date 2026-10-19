using System.ComponentModel.DataAnnotations;
using TutorScheduler.Core.Enums;

namespace TutorScheduler.Core.Entities;

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public UserRole Role { get; set; }

    // Populated only when Role == Tutor.
    public ICollection<TutorSubject> TutorSubjects { get; set; } = new List<TutorSubject>();
    public ICollection<LessonSlot> TutorSlots { get; set; } = new List<LessonSlot>();

    // Populated only when Role == Student.
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    // Concurrency token for the user's *schedule*. The per-slot Version only
    // stops two writes to the same slot; it can't stop one student booking two
    // different overlapping slots in parallel (or one tutor creating two
    // overlapping slots), because those writes touch different rows and each
    // overlap check runs before the other commits. Every schedule-changing
    // write therefore also rotates this value in the same SaveChangesAsync, so
    // two such writes for the same user can't both commit - the second one
    // gets DbUpdateConcurrencyException. Rotated in AppDbContext.SaveChangesAsync.
    [ConcurrencyCheck]
    public Guid ScheduleVersion { get; set; } = Guid.NewGuid();
}
