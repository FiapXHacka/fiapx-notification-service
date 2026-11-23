namespace FiapX.Notification.Domain;

public enum NotificationType
{
    VideoFailed,
    VideoCompleted,
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed,
}

public enum NotificationChannel
{
    Email,
}

/// <summary>
/// One message sent (or to be sent) to a user about a video.
/// (video id, type) is unique, so the same event never produces two e-mails (ADR-0012).
/// </summary>
public sealed class UserNotification
{
    private UserNotification()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid VideoId { get; private set; }
    public NotificationType Type { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public NotificationStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    public static UserNotification Create(Guid userId, Guid videoId, NotificationType type, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (videoId == Guid.Empty)
        {
            throw new ArgumentException("Video id is required.", nameof(videoId));
        }

        return new UserNotification
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            VideoId = videoId,
            Type = type,
            Channel = NotificationChannel.Email,
            Status = NotificationStatus.Pending,
            CreatedAt = now,
        };
    }

    public void MarkSent(DateTimeOffset now)
    {
        EnsurePending();
        Attempts++;
        Status = NotificationStatus.Sent;
        SentAt = now;
        LastError = null;
    }

    /// <summary>Records a failed attempt; after <paramref name="maxAttempts"/> the notification is given up.</summary>
    public void RecordFailure(string error, int maxAttempts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        EnsurePending();
        Attempts++;
        LastError = error;
        if (Attempts >= maxAttempts)
        {
            Status = NotificationStatus.Failed;
        }
    }

    private void EnsurePending()
    {
        if (Status != NotificationStatus.Pending)
        {
            throw new InvalidOperationException($"Notification {Id} is already {Status}.");
        }
    }
}
