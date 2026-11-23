using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FiapX.Notification.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef migrations</c>; no database connection is opened.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql("Host=localhost;Database=notification_db")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new NotificationDbContext(options);
    }
}
