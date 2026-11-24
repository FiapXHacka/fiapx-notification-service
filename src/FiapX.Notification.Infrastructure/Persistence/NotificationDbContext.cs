using FiapX.Notification.Domain;
using FiapX.Notification.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace FiapX.Notification.Infrastructure.Persistence;

/// <summary>notification_db (ADR-0009). The schema mirrors db/schema.sql.</summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<UserContact> UserContacts => Set<UserContact>();
    public DbSet<UserNotification> Notifications => Set<UserNotification>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<UserContact>(e =>
        {
            e.ToTable("user_contacts");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).ValueGeneratedNever();
            e.Property(x => x.Email).IsRequired();
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<UserNotification>(e =>
        {
            e.ToTable("notifications", t =>
            {
                t.HasCheckConstraint("ck_notifications_type", "type IN ('VIDEO_FAILED','VIDEO_COMPLETED')");
                t.HasCheckConstraint("ck_notifications_status", "status IN ('PENDING','SENT','FAILED')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Type).HasConversion(EnumNames.Converter<NotificationType>()).IsRequired();
            e.Property(x => x.Channel).HasConversion(EnumNames.Converter<NotificationChannel>())
                .HasDefaultValueSql("'EMAIL'").IsRequired();
            e.Property(x => x.Status).HasConversion(EnumNames.Converter<NotificationStatus>())
                .HasDefaultValueSql("'PENDING'").IsRequired();
            e.Property(x => x.Attempts).HasDefaultValue(0);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => new { x.VideoId, x.Type }).IsUnique().HasDatabaseName("ux_notifications_video_type");
            e.HasIndex(x => new { x.UserId, x.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_notifications_user");
        });

        modelBuilder.Entity<InboxMessage>(e =>
        {
            e.ToTable("inbox_messages");
            e.HasKey(x => new { x.MessageId, x.Consumer });
            e.Property(x => x.ProcessedAt).HasDefaultValueSql("now()");
        });
    }
}
