using System.ComponentModel.DataAnnotations;

namespace FiapX.Notification.Infrastructure.Messaging;

/// <summary>
/// Canonical RabbitMQ settings shared by the .NET services. Queue names and arguments are not here:
/// they are part of the topology contract (<see cref="RabbitMqTopology"/>).
/// </summary>
public sealed class RabbitMqOptions : IValidatableObject
{
    public const string Section = "RabbitMq";

    /// <summary>amqp://host:5672/vhost. Credentials come from the environment, never from appsettings.</summary>
    [Required]
    public string Uri { get; set; } = "amqp://localhost:5672/";

    /// <summary>Connection name shown in the RabbitMQ management UI.</summary>
    [Required]
    public string ClientName { get; set; } = "notification-service";

    [Required]
    public string Exchange { get; set; } = "fiapx.events";

    [Required]
    public string DeadLetterExchange { get; set; } = "fiapx.dlx";

    /// <summary>Unacknowledged messages the broker hands to this consumer.</summary>
    [Range(1, 1000)]
    public ushort PrefetchCount { get; set; } = 16;

    /// <summary>Messages handled in parallel (consumer dispatch concurrency).</summary>
    [Range(1, 64)]
    public ushort ConsumerConcurrency { get; set; } = 4;

    /// <summary>In-process attempts before a failing message is rejected to the dead-letter queue.</summary>
    [Range(1, 10)]
    public int MaxHandlerAttempts { get; set; } = 3;

    /// <summary>First retry delay; it doubles on each attempt (with jitter).</summary>
    public TimeSpan HandlerRetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (HandlerRetryDelay < TimeSpan.Zero || HandlerRetryDelay > TimeSpan.FromSeconds(5))
        {
            yield return new ValidationResult("HandlerRetryDelay must be between 0 and 5 seconds.", [nameof(HandlerRetryDelay)]);
        }
    }
}
