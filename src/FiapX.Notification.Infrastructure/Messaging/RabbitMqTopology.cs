using RabbitMQ.Client;

namespace FiapX.Notification.Infrastructure.Messaging;

/// <summary>
/// The part of the platform topology this service owns. It must match fiapx docs/architecture/04-mensageria.md
/// and local/rabbitmq/definitions.json argument by argument, or the broker answers PRECONDITION_FAILED.
/// </summary>
public static class RabbitMqTopology
{
    public const string Queue = "notification-service.events";
    public const string DeadLetterQueue = Queue + ".dlq";

    /// <summary>Guards against crash loops: deliveries lost with the connection count towards it.</summary>
    public const int DeliveryLimit = 5;

    /// <summary>Event types this service subscribes to (routing keys bound to its queue).</summary>
    public static readonly IReadOnlyList<string> Bindings =
    [
        "user.registered",
        "video.processed",
        "video.processing-failed",
    ];

    public static Dictionary<string, object?> QueueArguments(RabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-delivery-limit"] = DeliveryLimit,
            ["x-dead-letter-exchange"] = options.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = DeadLetterQueue,
        };
    }

    public static async Task DeclareAsync(IChannel channel, RabbitMqOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(options);

        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(options.DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(DeadLetterQueue, options.DeadLetterExchange, DeadLetterQueue,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: QueueArguments(options), cancellationToken: cancellationToken);
        foreach (var routingKey in Bindings)
        {
            await channel.QueueBindAsync(Queue, options.Exchange, routingKey, cancellationToken: cancellationToken);
        }
    }
}
