using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Enums;
using TutorScheduler.Data;
using Xunit;

namespace TutorScheduler.Tests.Concurrency;

// This is the proof required by the assignment: two independent DbContext
// instances load the same LessonSlot, both modify it, the first save wins,
// and the second gets DbUpdateConcurrencyException. SQLite (a real relational
// engine) is used instead of the EF Core InMemory provider so the WHERE-clause
// / affected-row-count mechanism is the same one Postgres uses in production.
public class LessonSlotConcurrencyTests
{
    private static async Task<(SqliteConnection Connection, DbContextOptions<AppDbContext> Options, int SlotId)> SeedSharedDatabaseAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        int slotId;
        using (var seedContext = new AppDbContext(options))
        {
            await seedContext.Database.EnsureCreatedAsync();

            var slot = new LessonSlot
            {
                Tutor = new User { FullName = "Tutor", Email = "tutor@test.com", PasswordHash = "x", Role = UserRole.Tutor },
                Subject = new Subject { Name = "Math" },
                StartTimeUtc = DateTime.UtcNow,
                EndTimeUtc = DateTime.UtcNow.AddHours(1),
                Status = SlotStatus.Available
            };

            seedContext.LessonSlots.Add(slot);
            await seedContext.SaveChangesAsync(CancellationToken.None);
            slotId = slot.Id;
        }

        return (connection, options, slotId);
    }

    [Fact]
    public async Task SecondConcurrentSave_ThrowsDbUpdateConcurrencyException()
    {
        var (connection, options, slotId) = await SeedSharedDatabaseAsync();
        using (connection)
        {
            // Two contexts = two independent "requests" reading the same row.
            using var contextA = new AppDbContext(options);
            using var contextB = new AppDbContext(options);

            var slotA = await contextA.LessonSlots.FirstAsync(s => s.Id == slotId);
            var slotB = await contextB.LessonSlots.FirstAsync(s => s.Id == slotId);

            slotA.Status = SlotStatus.Booked;
            slotB.Status = SlotStatus.Booked;

            // First writer: succeeds, and rotates Version in the database.
            await contextA.SaveChangesAsync(CancellationToken.None);

            // Second writer: its tracked Version is now stale, so EF Core's
            // WHERE Id = @id AND Version = @originalVersion matches zero rows.
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => contextB.SaveChangesAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task SaveAfterAnotherContextCommitted_SlotEndsUpBookedExactlyOnce()
    {
        var (connection, options, slotId) = await SeedSharedDatabaseAsync();
        using (connection)
        {
            using var contextA = new AppDbContext(options);
            using var contextB = new AppDbContext(options);

            var slotA = await contextA.LessonSlots.FirstAsync(s => s.Id == slotId);
            var slotB = await contextB.LessonSlots.FirstAsync(s => s.Id == slotId);

            slotA.Status = SlotStatus.Booked;
            slotB.Status = SlotStatus.Booked;

            await contextA.SaveChangesAsync(CancellationToken.None);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => contextB.SaveChangesAsync(CancellationToken.None));

            using var verifyContext = new AppDbContext(options);
            var finalSlot = await verifyContext.LessonSlots.AsNoTracking().FirstAsync(s => s.Id == slotId);

            Assert.Equal(SlotStatus.Booked, finalSlot.Status);
        }
    }
}
