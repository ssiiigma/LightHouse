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

    public DbSet<EmailToken> EmailTokens => Set<EmailToken>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<CalendarConnection> CalendarConnections => Set<CalendarConnection>();
    public DbSet<CalendarEventLink> CalendarEventLinks => Set<CalendarEventLink>();
    public DbSet<ModelAuditEntry> ModelAuditEntries => Set<ModelAuditEntry>();
    public DbSet<Backup> Backups => Set<Backup>();
    public DbSet<NotificationQueueItem> NotificationQueue => Set<NotificationQueueItem>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

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
            // TaskId / HabitId — логічні посилання без зовнішніх ключів: історія переживає видалення завдань.
        });

        b.Entity<RecommendationFeedback>(e =>
        {
            e.Property(x => x.RecommendationKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.ContextJson).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
            e.HasIndex(x => new { x.UserId, x.RecommendationKey }).IsUnique(); // повторний відгук ігнорується
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BanditState>(e =>
        {
            e.Property(x => x.SnapshotJson).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken(); // оптимістична конкурентність
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EmailToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DeviceToken>(e =>
        {
            e.Property(x => x.FcmToken).HasMaxLength(512).IsRequired();
            e.HasIndex(x => x.FcmToken).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CalendarConnection>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.GoogleAccountEmail).HasMaxLength(256).IsRequired();
            e.Property(x => x.RefreshTokenProtected).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.CalendarId).HasMaxLength(256).IsRequired();
            e.HasOne<User>().WithOne().HasForeignKey<CalendarConnection>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CalendarEventLink>(e =>
        {
            e.Property(x => x.GoogleEventId).HasMaxLength(1024).IsRequired();
            e.HasIndex(x => x.TaskId).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

            // NoAction: уникаємо кількох каскадних шляхів (User → CalendarEventLink та User → Task → CalendarEventLink).
            // Перед фізичним видаленням завдання фонова задача спершу видаляє його зв'язок із подією.
            e.HasOne<TaskItem>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<ModelAuditEntry>(e =>
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade));

        b.Entity<Backup>(e =>
        {
            e.Property(x => x.Content).HasColumnType("varbinary(max)").IsRequired();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<NotificationQueueItem>(e =>
        {
            e.ToTable("NotificationQueue");
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(500).IsRequired();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            // TaskId / HabitId — логічні посилання без зовнішніх ключів.
        });
    }
}