using Microsoft.AspNetCore.Identity;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Enums;
using TutorScheduler.Core.Interfaces;

namespace TutorScheduler.API;

// Idempotent: checks for the demo tutor before inserting anything, so this is
// safe to run on every startup. Runs through the normal repository/UnitOfWork
// path (not raw SQL or HasData) so it works identically regardless of provider,
// and passwords are hashed via the same IPasswordHasher used at registration.
public static class SeedData
{
    public const string DemoPassword = "Passw0rd!";

    public static async Task EnsureSeededAsync(IServiceProvider services)
    {
        var userRepository = services.GetRequiredService<IUserRepository>();
        var subjectRepository = services.GetRequiredService<ISubjectRepository>();
        var lessonSlotRepository = services.GetRequiredService<ILessonSlotRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var passwordHasher = services.GetRequiredService<IPasswordHasher<User>>();

        if (await userRepository.EmailExistsAsync("tutor@demo.com", CancellationToken.None))
        {
            return;
        }

        var tutor = new User { FullName = "Demo Tutor", Email = "tutor@demo.com", Role = UserRole.Tutor, PasswordHash = string.Empty };
        tutor.PasswordHash = passwordHasher.HashPassword(tutor, DemoPassword);
        userRepository.Add(tutor);

        var student = new User { FullName = "Demo Student", Email = "student@demo.com", Role = UserRole.Student, PasswordHash = string.Empty };
        student.PasswordHash = passwordHasher.HashPassword(student, DemoPassword);
        userRepository.Add(student);

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var subjects = new[] { "היסטוריה", "אנגלית", "מתמטיקה", "אדריכלות", "תכנות", "דקדוק" }
            .Select(name => new Subject { Name = name })
            .ToList();
        foreach (var s in subjects)
        {
            subjectRepository.Add(s);
        }
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var subject = subjects.Single(s => s.Name == "מתמטיקה");

        var slot = new LessonSlot
        {
            TutorId = tutor.Id,
            SubjectId = subject.Id,
            StartTimeUtc = DateTime.UtcNow.AddDays(1),
            EndTimeUtc = DateTime.UtcNow.AddDays(1).AddHours(1),
            Price = 120m,
            Status = SlotStatus.Available
        };
        lessonSlotRepository.Add(slot);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }
}
