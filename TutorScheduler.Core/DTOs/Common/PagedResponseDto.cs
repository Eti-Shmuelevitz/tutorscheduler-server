namespace TutorScheduler.Core.DTOs.Common;

// Shared shape for every paged listing endpoint (LessonSlots, Bookings, ...).
public class PagedResponseDto<T>
{
    public IReadOnlyList<T> Items { get; set; } = new List<T>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
