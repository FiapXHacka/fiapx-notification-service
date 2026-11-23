using System.Text;
using System.Text.Json;
using FiapX.Notification.Application.Messaging;
using RabbitMQ.Client;

namespace FiapX.Notification.IntegrationTests;

/// <summary>Test-side AMQP client: publishes like the other services do and inspects queues.</summary>
internal sealed class Broker : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;

    private Broker(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public static async Task<Broker> ConnectAsync(string uri)
    {
        var connection = await new ConnectionFactory { Uri = new Uri(uri) }.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        return new Broker(connection, channel);
    }

    public static byte[] Envelope(Guid id, string type, object data) =>
        JsonSerializer.SerializeToUtf8Bytes(new CloudEvent<object>
        {
            Id = id,
            Source = "fiapx/tests",
            Type = type,
            Time = DateTimeOffset.UtcNow,
            Subject = id.ToString(),
            DataSchema = $"fiapx/{type}/v1",
            Data = data,
        }, CloudEventJson.Options);

    public async Task PublishAsync(string exchange, string routingKey, byte[] body)
    {
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/cloudevents+json",
            MessageId = Guid.NewGuid().ToString(),
        };
        await _channel.BasicPublishAsync(exchange, routingKey, mandatory: false, properties, body);
    }

    public Task PublishEventAsync(Guid id, string type, object data) =>
        PublishAsync("fiapx.events", type, Envelope(id, type, data));

    public Task PublishRawToQueueAsync(string queue, string body) =>
        PublishAsync(string.Empty, queue, Encoding.UTF8.GetBytes(body));

    public async Task<uint> MessageCountAsync(string queue) =>
        (await _channel.QueueDeclarePassiveAsync(queue)).MessageCount;

    public async Task WaitForQueueAsync(string queue, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                await using var probe = await _connection.CreateChannelAsync();
                await probe.QueueDeclarePassiveAsync(queue);
                return;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
