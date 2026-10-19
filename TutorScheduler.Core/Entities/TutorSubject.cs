namespace TutorScheduler.Core.Entities;

// Explicit join entity for the Tutor <-> Subject many-to-many.
// Composite key (TutorId, SubjectId) is configured via Fluent API in AppDbContext.
public class TutorSubject
{
    public int TutorId { get; set; }
    public User Tutor { get; set; } = default!;

    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = default!;
}
