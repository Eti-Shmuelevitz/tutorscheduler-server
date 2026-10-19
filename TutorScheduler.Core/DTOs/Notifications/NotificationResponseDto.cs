using TutorScheduler.Core.Enums;

namespace TutorScheduler.Core.DTOs.Notifications;

public class NotificationResponseDto
{
    public int Id { get; set; }
    public NotificationType Type { get; set; }
    public int LessonSlotId { get; set; }
    public string SubjectName { get; set; } = default!;
    public string TutorName { get; set; } = default!;
    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsRead { get; set; }
}
