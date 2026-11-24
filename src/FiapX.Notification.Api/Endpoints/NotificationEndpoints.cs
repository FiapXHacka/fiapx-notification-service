using System.Security.Claims;
using FiapX.Notification.Domain;
using FiapX.Notification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FiapX.Notification.Api.Endpoints;

public sealed record NotificationResponse(
    Guid Id,
    Guid VideoId,
    NotificationType Type,
    NotificationStatus Status,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt);

public static class NotificationEndpoints
{
    public const int PageSize = 50;

    /// <summary>GET /notifications: the caller's latest notifications (user id from the token's sub claim).</summary>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/notifications", ListMineAsync).RequireAuthorization();
        return app;
    }

    internal static async Task<IResult> ListMineAsync(ClaimsPrincipal user, NotificationDbContext db, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirstValue("sub"), out var userId))
        {
            return TypedResults.Problem("The token has no valid sub claim.", statusCode: StatusCodes.Status403Forbidden);
        }

        var items = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(PageSize)
            .Select(n => new NotificationResponse(n.Id, n.VideoId, n.Type, n.Status, n.Attempts, n.CreatedAt, n.SentAt))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(items);
    }
}
