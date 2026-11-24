using System.Diagnostics.CodeAnalysis;

namespace FiapX.Notification.Application.Messaging;

/// <summary>
/// Handles one event type. It runs inside the consumer's database transaction, together with the
/// inbox record, so a redelivered message never applies its effect twice (ADR-0007).
/// Throwing makes the consumer nack the message: it is redelivered until the quorum delivery limit
/// sends it to the dead-letter queue.
/// </summary>
[SuppressMessage("Naming", "CA1711", Justification = "Handles integration events; unrelated to the System.EventHandler delegate.")]
public interface IIntegrationEventHandler
{
    /// <summary>The CloudEvent <c>type</c> this handler processes, e.g. <c>user.registered</c>.</summary>
    string EventType { get; }

    Task HandleAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken);
}
