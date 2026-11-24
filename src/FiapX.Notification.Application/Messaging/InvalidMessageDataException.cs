namespace FiapX.Notification.Application.Messaging;

/// <summary>
/// The event type is known but its data cannot be read or fails validation. Retrying cannot fix it,
/// so the consumer rejects the message straight to the dead-letter queue (ADR-0007).
/// Handlers throw it for any validation failure of the incoming data.
/// </summary>
public sealed class InvalidMessageDataException : Exception
{
    public InvalidMessageDataException()
    {
    }

    public InvalidMessageDataException(string message)
        : base(message)
    {
    }

    public InvalidMessageDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
