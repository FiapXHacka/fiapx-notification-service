using FiapX.Notification.Domain;
using Shouldly;

namespace FiapX.Notification.UnitTests.Domain;

public class UserNotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 18, 0, 0, TimeSpan.Zero);

    private static UserNotification NewNotification() =>
        UserNotification.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), NotificationType.VideoFailed, Now);

    [Fact]
    public void Create_StartsPendingByEmail()
    {
        var userId = Guid.CreateVersion7();
        var videoId = Guid.CreateVersion7();

        var n = UserNotification.Create(userId, videoId, NotificationType.VideoCompleted, Now);

        n.Id.ShouldNotBe(Guid.Empty);
        n.UserId.ShouldBe(userId);
        n.VideoId.ShouldBe(videoId);
        n.Type.ShouldBe(NotificationType.VideoCompleted);
        n.Channel.ShouldBe(NotificationChannel.Email);
        n.Status.ShouldBe(NotificationStatus.Pending);
        n.Attempts.ShouldBe(0);
        n.CreatedAt.ShouldBe(Now);
        n.SentAt.ShouldBeNull();
    }

    [Fact]
    public void Create_RejectsEmptyIds()
    {
        Should.Throw<ArgumentException>(() =>
            UserNotification.Create(Guid.Empty, Guid.CreateVersion7(), NotificationType.VideoFailed, Now));
        Should.Throw<ArgumentException>(() =>
            UserNotification.Create(Guid.CreateVersion7(), Guid.Empty, NotificationType.VideoFailed, Now));
    }

    [Fact]
    public void MarkSent_RecordsAttemptAndClearsError()
    {
        var n = NewNotification();
        n.RecordFailure("smtp timeout", maxAttempts: 3);

        n.MarkSent(Now.AddMinutes(1));

        n.Status.ShouldBe(NotificationStatus.Sent);
        n.Attempts.ShouldBe(2);
        n.SentAt.ShouldBe(Now.AddMinutes(1));
        n.LastError.ShouldBeNull();
    }

    [Fact]
    public void RecordFailure_GivesUpAfterMaxAttempts()
    {
        var n = NewNotification();

        n.RecordFailure("smtp timeout", maxAttempts: 2);
        n.Status.ShouldBe(NotificationStatus.Pending);
        n.RecordFailure("smtp refused", maxAttempts: 2);

        n.Status.ShouldBe(NotificationStatus.Failed);
        n.Attempts.ShouldBe(2);
        n.LastError.ShouldBe("smtp refused");
    }

    [Fact]
    public void FinishedNotification_CannotChangeAgain()
    {
        var n = NewNotification();
        n.MarkSent(Now);

        Should.Throw<InvalidOperationException>(() => n.MarkSent(Now));
        Should.Throw<InvalidOperationException>(() => n.RecordFailure("late", 3));
    }

    [Fact]
    public void RecordFailure_ValidatesArguments()
    {
        var n = NewNotification();

        Should.Throw<ArgumentException>(() => n.RecordFailure(" ", 3));
        Should.Throw<ArgumentOutOfRangeException>(() => n.RecordFailure("x", 0));
    }
}
