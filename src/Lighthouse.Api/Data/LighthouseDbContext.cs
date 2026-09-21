using Lighthouse.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Data;

public sealed class LighthouseDbContext(DbContextOptions<LighthouseDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<NotificationSettings> NotificationSettings => Set<NotificationSettings>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Subtask> Subtasks => Set<Subtask>();
    public DbSet<Habit> Habits => Set<Habit>();
    public DbSet<HabitLog> HabitLogs => Set<HabitLog>();
    public DbSet<BehaviorEvent> BehaviorEvents => Set<BehaviorEvent>();
    public DbSet<RecommendationFeedback> RecommendationFeedbacks => Set<RecommendationFeedback>();
    public DbSet<BanditState> BanditStates => Set<BanditState>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Ідентифікатори генерує клієнт (офлайн-створення), тому БД їх не генерує.
        foreach (var entityType in b.Model.GetEntityTypes()
                     .Where(t => typeof(BaseEntity).IsAssignableFrom(t.ClrType)))
        {
            b.Entity(entityType.ClrType).Property(nameof(BaseEntity.Id)).ValueGeneratedNever();
        }

        b.Entity<User>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(100);
            e.Property(x => x.GoogleSubject).HasMaxLength(64);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
            e.HasIndex(x => x.Email).IsUnique();
            e.HasOne(x => x.NotificationSettings)
                .WithOne()
                .HasForeignKey<NotificationSettings>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<NotificationSettings>(e => e.HasKey(x => x.UserId));

        b.Entity<RefreshToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.User)
                .WithMany(x => x.RefreshTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ColorHex).HasMaxLength(9);
            e.Property(x => x.Icon).HasMaxLength(50);
            e.HasIndex(x => new { x.UserId, x.ServerModifiedAtUtc });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TaskItem>(e =>
        {
            e.ToTable("Tasks");
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.HasIndex(x => new { x.UserId, x.ServerModifiedAtUtc });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

            // NoAction: SQL Server забороняє кілька каскадних шляхів (User → Category → Task та User → Task).
            // Категорії видаляються «м'яко», а завдання переносяться в «Без категорії» у сервісі.
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Subtasks).WithOne().HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Subtask>(e => e.Property(x => x.Title).HasMaxLength(200).IsRequired());

        b.Entity<Habit>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.ColorHex).HasMaxLength(9);
            e.Property(x => x.Icon).HasMaxLength(50);
            e.HasIndex(x => new { x.UserId, x.ServerModifiedAtUtc });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Logs).WithOne().HasForeignKey(x => x.HabitId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<HabitLog>(e => e.HasIndex(x => new { x.HabitId, x.Date }).IsUnique());

        b.Entity<BehaviorEvent>(e =>
        {
            e.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.UserId, x.OccurredAtUtc });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RecommendationFeedback>(e =>
        {
            e.Property(x => x.RecommendationKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.ContextJson).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BanditState>(e =>
        {
            e.Property(x => x.SnapshotJson).HasColumnType("nvarchar(max)").IsRequired();
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
