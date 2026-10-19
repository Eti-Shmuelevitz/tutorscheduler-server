using TutorScheduler.Core.Enums;

namespace TutorScheduler.Core.Entities;

// A message waiting in a user's personal area. Stores structured data (which
// lesson, the tutor's reason) rather than finished text, so the client can
// phrase it in its own language.
public class Notification
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    public NotificationType Type { get; set; }

    public int LessonSlotId { get; set; }
    public LessonSlot LessonSlot { get; set; } = default!;

    public string? Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    // Null until the user opens it.
    public DateTime? ReadAtUtc { get; set; }
}
