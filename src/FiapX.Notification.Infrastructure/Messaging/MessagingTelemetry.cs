using System.Diagnostics;

namespace FiapX.Notification.Infrastructure.Messaging;

public static class MessagingTelemetry
{
    public const string SourceName = "FiapX.Notification.Messaging";

    internal static readonly ActivitySource Source = new(SourceName);
}
