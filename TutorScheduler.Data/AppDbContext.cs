using Microsoft.EntityFrameworkCore;
using TutorScheduler.Core.Entities;
using TutorScheduler.Core.Enums;

namespace TutorScheduler.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<TutorSubject> TutorSubjects => Set<TutorSubject>();
    public DbSet<LessonSlot> LessonSlots => Set<LessonSlot>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();

            entity.Property(u => u.ScheduleVersion)
                .IsConcurrencyToken();
        });

        // Many-to-many: Tutor <-> Subject via the explicit TutorSubject join entity.
        modelBuilder.Entity<TutorSubject>(entity =>
        {
            entity.HasKey(ts => new { ts.TutorId, ts.SubjectId });

            entity.HasOne(ts => ts.Tutor)
                .WithMany(u => u.TutorSubjects)
                .HasForeignKey(ts => ts.TutorId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ts => ts.Subject)
                .WithMany(s => s.TutorSubjects)
                .HasForeignKey(ts => ts.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // One-to-many: Tutor -> LessonSlot. Restrict delete so removing a tutor
        // account can't silently cascade-delete a slot history.
        modelBuilder.Entity<LessonSlot>(entity =>
        {
            entity.HasOne(ls => ls.Tutor)
                .WithMany(u => u.TutorSlots)
                .HasForeignKey(ls => ls.TutorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(ls => ls.Subject)
                .WithMany(s => s.LessonSlots)
                .HasForeignKey(ls => ls.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // [ConcurrencyCheck] on LessonSlot.Version already marks this as a
            // concurrency token by convention; this line just makes it explicit.
            entity.Property(ls => ls.Version)
                .IsConcurrencyToken();
        });

        // One-to-many: LessonSlot -> Booking (see comment on LessonSlot.Bookings
        // for why this isn't a 1:1).
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasOne(b => b.LessonSlot)
                .WithMany(ls => ls.Bookings)
                .HasForeignKey(b => b.LessonSlotId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(b => b.Student)
                .WithMany(u => u.Bookings)
                .HasForeignKey(b => b.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Last line of defense, not the primary mechanism: the Version
            // token already makes a second active booking on a slot
            // impossible through the Service layer. This partial unique index
            // guarantees it at the database level too, even for a write that
            // bypasses the Service (manual SQL, a future bug). Cancelled rows
            // are excluded so cancel-then-rebook history still works.
            entity.HasIndex(b => b.LessonSlotId)
                .IsUnique()
                .HasFilter($"\"Status\" = {(int)BookingStatus.Active}")
                .HasDatabaseName("IX_Bookings_LessonSlotId_ActiveUnique");
        });

        // One-to-many: User -> Notification, and each notification points at
        // the lesson it's about. Slots with booking history are only ever
        // soft-deleted, so in practice the slot cascade never fires.
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(n => n.LessonSlot)
                .WithMany()
                .HasForeignKey(n => n.LessonSlotId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(n => n.Reason).HasMaxLength(300);

            // Serves both the "my notifications" list and the unread badge.
            entity.HasIndex(n => new { n.UserId, n.ReadAtUtc });
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        RotateConcurrencyTokens();
        return base.SaveChangesAsync(cancellationToken);
    }

    // EF writes the tracked *original* Version into the UPDATE's WHERE clause
    // (because it's a concurrency token) and the *current* Version — set here —
    // into the SET clause. If another transaction already rotated the row's
    // Version and committed, this WHERE clause matches zero rows and EF throws
    // DbUpdateConcurrencyException. Centralizing the rotation here means no
    // individual Service method has to remember to do it.
    private void RotateConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries<LessonSlot>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.Version = Guid.NewGuid();
            }
        }

        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.ScheduleVersion = Guid.NewGuid();
            }
        }
    }
}
