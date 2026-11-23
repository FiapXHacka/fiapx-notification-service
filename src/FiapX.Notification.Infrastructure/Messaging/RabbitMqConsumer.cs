using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FiapX.Notification.Infrastructure.Messaging;

/// <summary>
/// Consumes the service queue with manual acks: ack only after the dispatcher committed,
/// nack with requeue on any failure. Reconnects (and re-declares the topology) when the channel drops.
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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilClosedAsync(stoppingToken);
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

    private async Task ConsumeUntilClosedAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var conn = await connection.GetAsync(stoppingToken);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitMqTopology.DeclareAsync(channel, settings, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: settings.Prefetch, global: false, stoppingToken);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(channel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(settings.Queue, autoAck: false, consumer, stoppingToken);
        LogConsuming(settings.Queue);

        await closed.Task.WaitAsync(stoppingToken);
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        try
        {
            await dispatcher.DispatchAsync(delivery.Body, stoppingToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
#pragma warning disable CA1031 // Any failure means "try again later": nack and let the delivery limit decide.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogProcessingFailed(ex, delivery.BasicProperties.MessageId, delivery.Redelivered);
            if (channel.IsOpen)
            {
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consuming queue {Queue}")]
    private partial void LogConsuming(string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "RabbitMQ connection lost, reconnecting in {DelaySeconds}s")]
    private partial void LogConnectionLost(Exception exception, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Processing failed for message {MessageId} (redelivered: {Redelivered}); nacked")]
    private partial void LogProcessingFailed(Exception exception, string? messageId, bool redelivered);
}
