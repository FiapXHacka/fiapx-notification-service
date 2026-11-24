using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FiapX.Notification.Application.Messaging;
using FiapX.Notification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FiapX.Notification.Infrastructure.Messaging;

/// <summary>What the consumer must answer to the broker (ADR-0007).</summary>
public enum DispatchOutcome
{
    /// <summary>Handler ran and the inbox record was committed: ack.</summary>
    Processed,

    /// <summary>The inbox already had this message id: ack.</summary>
    Duplicate,

    /// <summary>Envelope without id/type, or a type this service does not handle: ack and warn (tolerant reader).</summary>
    Ignored,

    /// <summary>Invalid data, or the handler kept failing after the in-process retries: reject to the DLQ.</summary>
    Rejected,
}

/// <summary>
/// Turns a raw AMQP body into a handler call, inside a transaction shared with the inbox. Handler failures are
/// retried in process (fresh DI scope and transaction each time) with exponential backoff and jitter, because
/// a requeued nack would not count towards the queue delivery limit and could loop forever.
/// Only cancellation (shutdown) propagates as an exception: the consumer then nacks with requeue.
/// </summary>
public sealed partial class MessageDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<MessageDispatcher> logger)
{
    /// <summary>The consumer only starts when at least one handler is registered; otherwise messages wait in the queue.</summary>
    public bool HasHandlers()
    {
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetServices<IIntegrationEventHandler>().Any();
    }

    public async Task<DispatchOutcome> DispatchAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var envelope = TryParse(body);
        if (envelope is null)
        {
            return DispatchOutcome.Ignored;
        }

        using var activity = StartActivity(envelope);
        var settings = options.Value;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var outcome = await ProcessOnceAsync(envelope, cancellationToken);
                LogDispatched(envelope.Id, envelope.Type, outcome, attempt);
                return outcome;
            }
            catch (InvalidMessageDataException ex)
            {
                LogInvalidData(ex, envelope.Id, envelope.Type);
                return DispatchOutcome.Rejected;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // Any handler failure is retried, then dead-lettered; it must not crash the consumer.
            catch (Exception ex) when (attempt < settings.MaxHandlerAttempts)
            {
                LogRetrying(ex, envelope.Id, envelope.Type, attempt, settings.MaxHandlerAttempts);
                await Task.Delay(RetryDelay(settings.HandlerRetryDelay, attempt), timeProvider, cancellationToken);
            }
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogGaveUp(ex, envelope.Id, envelope.Type, attempt);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return DispatchOutcome.Rejected;
            }
        }
    }

    /// <summary>baseDelay × 2^(attempt-1) × jitter in [0.8, 1.2] (200 ms → ~0.2 s, 0.4 s, 0.8 s).</summary>
    internal static TimeSpan RetryDelay(TimeSpan baseDelay, int attempt) =>
        RetryDelay(baseDelay, attempt, RandomNumberGenerator.GetInt32(800, 1201) / 1000.0);

    internal static TimeSpan RetryDelay(TimeSpan baseDelay, int attempt, double jitter) =>
        TimeSpan.FromTicks((long)(baseDelay.Ticks * Math.Pow(2, attempt - 1) * jitter));

    private async Task<DispatchOutcome> ProcessOnceAsync(CloudEvent<JsonElement> envelope, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler>()
            .FirstOrDefault(h => h.EventType == envelope.Type);
        if (handler is null)
        {
            LogNotHandled(envelope.Id, envelope.Type);
            return DispatchOutcome.Ignored;
        }

        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Copies of the same message can be handled in parallel (ConsumerConcurrency). A transaction-scoped
            // advisory lock per message id makes the second copy wait for the first commit and see it in the inbox.
            var lockKey = envelope.Id.ToString();
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);

            var alreadyProcessed = await db.InboxMessages
                .AnyAsync(m => m.MessageId == envelope.Id && m.Consumer == RabbitMqTopology.Queue, ct);
            if (alreadyProcessed)
            {
                return DispatchOutcome.Duplicate;
            }

            await handler.HandleAsync(new IntegrationEvent(envelope), ct);
            db.InboxMessages.Add(new InboxMessage(envelope.Id, RabbitMqTopology.Queue, timeProvider.GetUtcNow()));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return DispatchOutcome.Processed;
        }, cancellationToken);
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

            LogNotACloudEvent(null);
        }
        catch (JsonException ex)
        {
            LogNotACloudEvent(ex);
        }

        return null;
    }

    private static Activity? StartActivity(CloudEvent<JsonElement> envelope)
    {
        ActivityContext.TryParse(envelope.TraceParent, null, out var parent);
        var activity = MessagingTelemetry.Source.StartActivity($"process {envelope.Type}", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", RabbitMqTopology.Queue);
        activity?.SetTag("messaging.message.id", envelope.Id.ToString());
        activity?.SetTag("cloudevents.event_type", envelope.Type);
        activity?.SetTag("cloudevents.event_subject", envelope.Subject);
        return activity;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring message without a valid CloudEvent id/type")]
    private partial void LogNotACloudEvent(Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring message {MessageId}: type {EventType} is not handled by this service")]
    private partial void LogNotHandled(Guid messageId, string eventType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rejecting message {MessageId} ({EventType}) to the DLQ: invalid data")]
    private partial void LogInvalidData(Exception exception, Guid messageId, string eventType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handler failed for message {MessageId} ({EventType}), attempt {Attempt} of {MaxAttempts}; retrying")]
    private partial void LogRetrying(Exception exception, Guid messageId, string eventType, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rejecting message {MessageId} ({EventType}) to the DLQ after {Attempts} failed attempts")]
    private partial void LogGaveUp(Exception exception, Guid messageId, string eventType, int attempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Message {MessageId} ({EventType}): {Outcome} on attempt {Attempt}")]
    private partial void LogDispatched(Guid messageId, string eventType, DispatchOutcome outcome, int attempt);
}
