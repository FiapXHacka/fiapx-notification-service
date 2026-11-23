using FiapX.Notification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FiapX.Notification.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ConsumerTests(ApiFixture fixture)
{
    private const string Queue = "notification-service.events";
    private const string DeadLetterQueue = "notification-service.events.dlq";

    private async Task<Broker> ConnectAsync()
    {
        var broker = await Broker.ConnectAsync(fixture.RabbitMq.GetConnectionString());
        await broker.WaitForQueueAsync(Queue, TimeSpan.FromSeconds(30));
        return broker;
    }

    private async Task<T> QueryAsync<T>(Func<NotificationDbContext, Task<T>> query)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<NotificationDbContext>());
    }

    [Fact]
    public async Task Topology_IsDeclaredWithDeadLetterQueue()
    {
        await using var broker = await ConnectAsync();

        (await broker.MessageCountAsync(Queue)).ShouldBeGreaterThanOrEqualTo(0u);
        (await broker.MessageCountAsync(DeadLetterQueue)).ShouldBeGreaterThanOrEqualTo(0u);
    }

    [Fact]
    public async Task Event_IsProcessedOnce_EvenWhenDeliveredTwice()
    {
        await using var broker = await ConnectAsync();
        var messageId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var data = new { userId, email = "ana@fiapx.dev", name = "Ana" };

        await broker.PublishEventAsync(messageId, "user.registered", data);
        await broker.PublishEventAsync(messageId, "user.registered", data);

        await Eventually.TrueAsync(
            () => QueryAsync(db => db.UserContacts.AnyAsync(c => c.UserId == userId)),
            "contact projected from user.registered");
        await Eventually.TrueAsync(async () => await broker.MessageCountAsync(Queue) == 0, "queue drained");

        var inboxRows = await QueryAsync(db => db.InboxMessages.CountAsync(m => m.MessageId == messageId));
        inboxRows.ShouldBe(1);
        var contact = await QueryAsync(db => db.UserContacts.SingleAsync(c => c.UserId == userId));
        contact.Email.ShouldBe("ana@fiapx.dev");
    }

    [Fact]
    public async Task UnsubscribedType_IsAckedAndIgnored()
    {
        await using var broker = await ConnectAsync();
        var messageId = Guid.CreateVersion7();

        await broker.PublishAsync(string.Empty, Queue, Broker.Envelope(messageId, "video.uploaded", new { }));

        await Eventually.TrueAsync(async () => await broker.MessageCountAsync(Queue) == 0, "message acked");
        (await QueryAsync(db => db.InboxMessages.AnyAsync(m => m.MessageId == messageId))).ShouldBeFalse();
    }

    [Fact]
    public async Task MalformedMessage_IsAckedAndDropped()
    {
        await using var broker = await ConnectAsync();
        var before = await broker.MessageCountAsync(DeadLetterQueue);

        await broker.PublishRawToQueueAsync(Queue, "this is not a cloud event");

        await Eventually.TrueAsync(async () => await broker.MessageCountAsync(Queue) == 0, "malformed message acked");
        (await broker.MessageCountAsync(DeadLetterQueue)).ShouldBe(before);
    }

    [Fact]
    public async Task FailingHandler_SendsTheMessageToTheDeadLetterQueue()
    {
        await using var broker = await ConnectAsync();
        var before = await broker.MessageCountAsync(DeadLetterQueue);
        var messageId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        await broker.PublishEventAsync(messageId, "user.registered",
            new { userId, email = "x@fiapx.dev", name = UserRegisteredTestHandler.FailingName });

        await Eventually.TrueAsync(async () => await broker.MessageCountAsync(DeadLetterQueue) > before,
            $"message dead-lettered after {ApiFixture.DeliveryLimit} deliveries");
        (await QueryAsync(db => db.UserContacts.AnyAsync(c => c.UserId == userId))).ShouldBeFalse();
        (await QueryAsync(db => db.InboxMessages.AnyAsync(m => m.MessageId == messageId))).ShouldBeFalse();
    }
}
