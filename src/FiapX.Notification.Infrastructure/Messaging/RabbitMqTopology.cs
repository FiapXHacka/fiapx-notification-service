using RabbitMQ.Client;

namespace FiapX.Notification.Infrastructure.Messaging;

/// <summary>
/// The part of the platform topology this service owns (fiapx/docs/architecture/04-mensageria.md).
/// Declarations are idempotent: every service declares the shared exchanges with the same settings.
/// </summary>
public static class RabbitMqTopology
{
    public const string EventsExchange = "fiapx.events";
    public const string DeadLetterExchange = "fiapx.dlx";

    /// <summary>Event types this service subscribes to (routing keys bound to its queue).</summary>
    public static readonly IReadOnlyList<string> Bindings =
    [
        "user.registered",
        "video.processed",
        "video.processing-failed",
    ];

    public static string DeadLetterQueue(string queue) => $"{queue}.dlq";

    public static Dictionary<string, object?> QueueArguments(RabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-delivery-limit"] = options.DeliveryLimit,
            ["x-dead-letter-exchange"] = DeadLetterExchange,
            ["x-dead-letter-routing-key"] = DeadLetterQueue(options.Queue),
        };
    }

    public static async Task DeclareAsync(IChannel channel, RabbitMqOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(options);

        await channel.ExchangeDeclareAsync(EventsExchange, ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);

        var deadLetterQueue = DeadLetterQueue(options.Queue);
        await channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(deadLetterQueue, DeadLetterExchange, deadLetterQueue,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(options.Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: QueueArguments(options), cancellationToken: cancellationToken);
        foreach (var routingKey in Bindings)
        {
            await channel.QueueBindAsync(options.Queue, EventsExchange, routingKey,
                cancellationToken: cancellationToken);
        }
    }
}
