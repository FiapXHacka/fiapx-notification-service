using FiapX.Notification.Infrastructure.Messaging;
using FiapX.Notification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FiapX.Notification.IntegrationTests;

/// <summary>The failure handling table of ADR-0007, against a real RabbitMQ 4.2 and Postgres.</summary>
[Collection(ApiCollection.Name)]
public class ConsumerTests(ApiFixture fixture)
{
    private const string Queue = RabbitMqTopology.Queue;
    private const string DeadLetterQueue = RabbitMqTopology.DeadLetterQueue;

    private async Task<Broker> ConnectAsync(string? vhost = null)
    {
        var broker = await Broker.ConnectAsync(fixture.RabbitMqUri(vhost));
        await broker.WaitForQueueAsync(Queue, TimeSpan.FromSeconds(30));
        return broker;
    }

    private async Task<T> QueryAsync<T>(Func<NotificationDbContext, Task<T>> query)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<NotificationDbContext>());
    }

    private static int AttemptsOf(Guid messageId) =>
        UserRegisteredTestHandler.Attempts.TryGetValue(messageId, out var n) ? n : 0;

    [Fact]
    public async Task Topology_IsDeclaredWithItsDeadLetterQueue()
    {
        await using var broker = await ConnectAsync();

        (await broker.InspectAsync(Queue)).ConsumerCount.ShouldBe(1u);
        await broker.InspectAsync(DeadLetterQueue);
    }

    [Fact]
    public async Task DuplicateMessageId_RunsTheHandlerOnce()
    {
        await using var broker = await ConnectAsync();
        var messageId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var data = new { userId, email = "ana@fiapx.dev", name = "Ana" };

        await broker.PublishEventAsync(messageId, "user.registered", data);
        await broker.PublishEventAsync(messageId, "user.registered", data);

        await Eventually.TrueAsync(() => QueryAsync(db => db.UserContacts.AnyAsync(c => c.UserId == userId)), "contact projected");
        await Eventually.TrueAsync(async () => await broker.MessageCountAsync(Queue) == 0, "both copies acked");
        AttemptsOf(messageId).ShouldBe(1);
        (await QueryAsync(db => db.InboxMessages.CountAsync(m => m.MessageId == messageId))).ShouldBe(1);
    }

    [Fact]
    public async Task UnknownType_IsAckedAndNotDeadLettered()
    {
        await using var broker = await ConnectAsync();
        var messageId = Guid.CreateVersion7();

        await broker.PublishAsync(string.Empty, Queue, Broker.Envelope(messageId, "video.uploaded", new { }));

        await Eventually.TrueAsync(async () => await broker.MessageCountAsync(Queue) == 0, "message acked");
        (await broker.DeadLetteredAsync(DeadLetterQueue, messageId)).ShouldBeFalse();
        (await QueryAsync(db => db.InboxMessages.AnyAsync(m => m.MessageId == messageId))).ShouldBeFalse();
    }

    [Fact]
    public async Task EnvelopeWithoutIdOrType_IsAcked()
    {
        await using var broker = await ConnectAsync();

        await broker.PublishRawToQueueAsync(Queue, "this is not a cloud event");

        await Eventually.TrueAsync(async () => await broker.MessageCountAsync(Queue) == 0, "message acked");
    }

    [Fact]
    public async Task MalformedDataForAKnownType_GoesToTheDlqImmediately()
    {
        await using var broker = await ConnectAsync();
        var messageId = Guid.CreateVersion7();

        await broker.PublishEventAsync(messageId, "user.registered", new { userId = "not-a-guid", email = "x@fiapx.dev", name = "X" });

        await Eventually.TrueAsync(() => broker.DeadLetteredAsync(DeadLetterQueue, messageId), "rejected to the DLQ");
        AttemptsOf(messageId).ShouldBe(1);
    }

    [Fact]
    public async Task InvalidDataForTheDomain_GoesToTheDlqImmediately()
    {
        await using var broker = await ConnectAsync();
        var messageId = Guid.CreateVersion7();

        await broker.PublishEventAsync(messageId, "user.registered", new { userId = Guid.CreateVersion7(), email = "no-at-sign", name = "X" });

        await Eventually.TrueAsync(() => broker.DeadLetteredAsync(DeadLetterQueue, messageId), "rejected to the DLQ");
        AttemptsOf(messageId).ShouldBe(1);
    }

    [Fact]
    public async Task HandlerThatAlwaysThrows_IsDeadLetteredAfterMaxAttempts_WithoutLooping()
    {
        await using var broker = await ConnectAsync();
        var messageId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        await broker.PublishEventAsync(messageId, "user.registered",
            new { userId, email = "x@fiapx.dev", name = UserRegisteredTestHandler.FailingName });

        await Eventually.TrueAsync(() => broker.DeadLetteredAsync(DeadLetterQueue, messageId), "rejected to the DLQ");
        AttemptsOf(messageId).ShouldBe(3, "RabbitMq:MaxHandlerAttempts defaults to 3");

        // No redelivery loop: the count stays put and the queue is empty.
        await Task.Delay(TimeSpan.FromSeconds(2));
        AttemptsOf(messageId).ShouldBe(3);
        (await broker.MessageCountAsync(Queue)).ShouldBe(0u);
        (await QueryAsync(db => db.UserContacts.AnyAsync(c => c.UserId == userId))).ShouldBeFalse();
    }

    [Fact]
    public async Task WithoutHandlers_NoConsumerIsAttached_AndMessagesWaitInTheQueue()
    {
        await using var broker = await ConnectAsync(ApiFixture.NoHandlersVhost);
        var messageId = Guid.CreateVersion7();

        await broker.PublishEventAsync(messageId, "user.registered", new { userId = Guid.CreateVersion7(), email = "a@fiapx.dev", name = "A" });
        await Task.Delay(TimeSpan.FromSeconds(2));

        var queue = await broker.InspectAsync(Queue);
        queue.ConsumerCount.ShouldBe(0u);
        queue.MessageCount.ShouldBe(1u);
        (await broker.MessageCountAsync(DeadLetterQueue)).ShouldBe(0u);
    }
}
