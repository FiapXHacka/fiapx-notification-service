using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FiapX.Notification.Infrastructure.Messaging;

/// <summary>
/// Declares the topology and consumes the service queue with manual acknowledgements (ADR-0007):
/// ack when processed, duplicated or ignored; reject (no requeue, so it goes to the DLQ) when the data is invalid
/// or the handler kept failing; nack with requeue only when shutting down. A requeued nack is never used for a
/// failure because it does not count towards the delivery limit.
/// Without registered handlers it declares the queue but does not consume, so messages wait safely.
/// </summary>
public sealed partial class RabbitMqConsumer(
    RabbitMqConnection connection,
    MessageDispatcher dispatcher,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consume = dispatcher.HasHandlers();
        if (!consume)
        {
            LogNoHandlers(RabbitMqTopology.Queue);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunUntilClosedAsync(consume, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // The loop must survive any broker failure and reconnect.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogConnectionLost(ex, ReconnectDelay.TotalSeconds);
            }

            await Task.Delay(ReconnectDelay, stoppingToken);
        }
    }

    private async Task RunUntilClosedAsync(bool consume, CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var conn = await connection.GetAsync(stoppingToken);
        await using var channel = await conn.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: settings.ConsumerConcurrency),
            stoppingToken);
        await RabbitMqTopology.DeclareAsync(channel, settings, stoppingToken);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        if (consume)
        {
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: settings.PrefetchCount, global: false, stoppingToken);
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(channel, delivery, stoppingToken);
            await channel.BasicConsumeAsync(RabbitMqTopology.Queue, autoAck: false, consumer, stoppingToken);
            LogConsuming(RabbitMqTopology.Queue, settings.PrefetchCount, settings.ConsumerConcurrency);
        }

        await closed.Task.WaitAsync(stoppingToken);
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        try
        {
            var outcome = await dispatcher.DispatchAsync(delivery.Body, stoppingToken);
            if (outcome == DispatchOutcome.Rejected)
            {
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None);
            }
            else
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown is not a failure: give the message back without counting a delivery attempt.
            if (channel.IsOpen)
            {
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
            }
        }
#pragma warning disable CA1031 // e.g. the channel closed while acking: the broker redelivers and the inbox dedups.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogSettleFailed(ex, delivery.BasicProperties.MessageId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consuming queue {Queue} (prefetch {Prefetch}, concurrency {Concurrency})")]
    private partial void LogConsuming(string queue, ushort prefetch, ushort concurrency);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No event handlers registered: queue {Queue} is declared but not consumed; messages wait in it")]
    private partial void LogNoHandlers(string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "RabbitMQ connection lost, reconnecting in {DelaySeconds}s")]
    private partial void LogConnectionLost(Exception exception, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not settle message {MessageId}; the broker will redeliver it")]
    private partial void LogSettleFailed(Exception exception, string? messageId);
}
