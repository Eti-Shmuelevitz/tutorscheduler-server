using System.ComponentModel.DataAnnotations;
using TutorScheduler.Core.Enums;

namespace TutorScheduler.Core.Entities;

// The contested resource: one tutor, one hour. Status flips Available -> Booked
// under the optimistic-concurrency check in the Service layer.
public class LessonSlot
{
    public int Id { get; set; }

    public int TutorId { get; set; }
    public User Tutor { get; set; } = default!;

    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = default!;

    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }

    public SlotStatus Status { get; set; } = SlotStatus.Available;

    // Set by the tutor when publishing the slot, in ILS. 0 = free lesson.
    public decimal Price { get; set; }

    // One-to-many, not one-to-one: a cancelled + rebooked slot needs a new
    // Booking row so the history isn't overwritten. "At most one Active
    // booking per slot" is a Service-layer business rule, not a DB constraint.
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    // Concurrency token. PostgreSQL has no rowversion, so this is rotated by hand
    // in AppDbContext.SaveChangesAsync before every write, not per-service.
    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
}
