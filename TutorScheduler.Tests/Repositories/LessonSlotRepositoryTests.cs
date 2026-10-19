using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Enums;
using TutorScheduler.Data;
using TutorScheduler.Data.Repositories;
using Xunit;

namespace TutorScheduler.Tests.Repositories;

public class LessonSlotRepositoryTests
{
    [Fact]
    public async Task GetAvailablePaged_ExcludesSlotsThatAlreadyStarted()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;

        using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var tutor = new User { FullName = "Tutor", Email = "tutor@test.com", PasswordHash = "x", Role = UserRole.Tutor };
        var subject = new Subject { Name = "Math" };
        var now = DateTime.UtcNow;
        LessonSlot Slot(DateTime start, SlotStatus status = SlotStatus.Available) => new()
        {
            Tutor = tutor,
            Subject = subject,
            StartTimeUtc = start,
            EndTimeUtc = start.AddHours(1),
            Status = status
        };

        var future = Slot(now.AddDays(1));
        context.LessonSlots.AddRange(
            Slot(now.AddDays(-2)),                // long past
            Slot(now.AddMinutes(-10)),            // in progress
            future,
            Slot(now.AddDays(2), SlotStatus.Booked));
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new LessonSlotRepository(context);
        var (items, totalCount) = await repository.GetAvailablePagedAsync(1, 20, null, null, false, CancellationToken.None);

        Assert.Equal(1, totalCount);
        Assert.Equal(future.Id, Assert.Single(items).Id);
    }
}
