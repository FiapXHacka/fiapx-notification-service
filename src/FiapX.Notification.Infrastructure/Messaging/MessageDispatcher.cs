using System.Diagnostics;
using System.Text.Json;
using FiapX.Notification.Application.Messaging;
using FiapX.Notification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FiapX.Notification.Infrastructure.Messaging;

public enum DispatchOutcome
{
    /// <summary>Handler ran and the inbox record was committed.</summary>
    Processed,

    /// <summary>The inbox already had this message id: redelivery, nothing done.</summary>
    Duplicate,

    /// <summary>Not a valid CloudEvent: it can never succeed, so it is acked and logged.</summary>
    Malformed,

    /// <summary>A type this service does not subscribe to: acked and logged.</summary>
    Ignored,
}

/// <summary>
/// Turns a raw AMQP body into a handler call, inside a transaction shared with the inbox (ADR-0007).
/// Returning means "ack"; throwing means "nack and requeue" (the quorum delivery limit then dead-letters it).
/// </summary>
public sealed partial class MessageDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<MessageDispatcher> logger)
{
    public async Task<DispatchOutcome> DispatchAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var envelope = TryParse(body);
        if (envelope is null)
        {
            return DispatchOutcome.Malformed;
        }

        using var activity = StartActivity(envelope);
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler>()
            .FirstOrDefault(h => h.EventType == envelope.Type);

        if (handler is null)
        {
            if (RabbitMqTopology.Bindings.Contains(envelope.Type))
            {
                // Subscribed but not handled yet: fail loudly so the message goes to the DLQ instead of being lost.
                throw new InvalidOperationException(
                    $"No handler registered for subscribed event type '{envelope.Type}' (message {envelope.Id}).");
            }

            LogIgnored(envelope.Id, envelope.Type);
            return DispatchOutcome.Ignored;
        }

        var consumer = options.Value.ConsumerName;
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        var outcome = await strategy.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var alreadyProcessed = await db.InboxMessages
                .AnyAsync(m => m.MessageId == envelope.Id && m.Consumer == consumer, ct);
            if (alreadyProcessed)
            {
                return DispatchOutcome.Duplicate;
            }

            await handler.HandleAsync(new IntegrationEvent(envelope), ct);
            db.InboxMessages.Add(new InboxMessage(envelope.Id, consumer, timeProvider.GetUtcNow()));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return DispatchOutcome.Processed;
        }, cancellationToken);

        LogDispatched(envelope.Id, envelope.Type, outcome);
        return outcome;
    }

    private CloudEvent<JsonElement>? TryParse(ReadOnlyMemory<byte> body)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<CloudEvent<JsonElement>>(body.Span, CloudEventJson.Options);
            if (envelope is not null && envelope.Id != Guid.Empty && !string.IsNullOrWhiteSpace(envelope.Type))
            {
                return envelope;
            }

            LogMalformed(null);
        }
        catch (JsonException ex)
        {
            LogMalformed(ex);
        }

        return null;
    }

    private static Activity? StartActivity(CloudEvent<JsonElement> envelope)
    {
        ActivityContext.TryParse(envelope.TraceParent, null, out var parent);
        var activity = MessagingTelemetry.Source.StartActivity($"process {envelope.Type}", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.message.id", envelope.Id.ToString());
        activity?.SetTag("cloudevents.event_type", envelope.Type);
        activity?.SetTag("cloudevents.event_subject", envelope.Subject);
        return activity;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Discarding message that is not a valid CloudEvent")]
    private partial void LogMalformed(Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring message {MessageId} of unsubscribed type {EventType}")]
    private partial void LogIgnored(Guid messageId, string eventType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Message {MessageId} ({EventType}): {Outcome}")]
    private partial void LogDispatched(Guid messageId, string eventType, DispatchOutcome outcome);
}
