namespace TutorScheduler.Core.Enums;

public enum BookingStatus
{
    Active,
    Cancelled,

    // The tutor cancelled the lesson itself; the student gets a Notification.
    // Kept separate from Cancelled so the student's history can say who cancelled.
    CancelledByTutor
}
