using System.ComponentModel.DataAnnotations;

namespace FiapX.Notification.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string Section = "RabbitMq";

    /// <summary>amqp://user:password@host:5672/vhost. Credentials come from the environment, never from appsettings.</summary>
    [Required]
    public string Uri { get; set; } = "amqp://localhost:5672/";

    /// <summary>Name recorded in the inbox and shown as the connection name in RabbitMQ.</summary>
    [Required]
    public string ConsumerName { get; set; } = "notification-service";

    [Required]
    public string Queue { get; set; } = "notification-service.events";

    /// <summary>Redeliveries before the quorum queue dead-letters a message (x-delivery-limit).</summary>
    [Range(1, 100)]
    public int DeliveryLimit { get; set; } = 5;

    [Range(1, 1000)]
    public ushort Prefetch { get; set; } = 10;

    /// <summary>Lets tools (migrations, tests) run the API without consuming.</summary>
    public bool ConsumerEnabled { get; set; } = true;
}
