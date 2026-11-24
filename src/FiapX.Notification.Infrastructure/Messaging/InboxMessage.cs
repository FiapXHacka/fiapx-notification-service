namespace FiapX.Notification.Infrastructure.Messaging;

/// <summary>
/// Marks a message as processed by a consumer. It is written in the same transaction as the handler's
/// effect, so a redelivery of the same CloudEvent id is detected and skipped (ADR-0007).
/// </summary>
public sealed class InboxMessage
{
    public InboxMessage(Guid messageId, string consumer, DateTimeOffset processedAt)
    {
        MessageId = messageId;
        Consumer = consumer;
        ProcessedAt = processedAt;
    }

    public Guid MessageId { get; private set; }
    public string Consumer { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }
}
