namespace TutorScheduler.Core.Entities;

public class Subject
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;

    public ICollection<TutorSubject> TutorSubjects { get; set; } = new List<TutorSubject>();
    public ICollection<LessonSlot> LessonSlots { get; set; } = new List<LessonSlot>();
}
