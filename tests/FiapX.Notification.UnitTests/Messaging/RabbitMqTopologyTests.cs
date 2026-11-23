using FiapX.Notification.Infrastructure.Messaging;
using Shouldly;

namespace FiapX.Notification.UnitTests.Messaging;

public class RabbitMqTopologyTests
{
    [Fact]
    public void QueueArguments_DeclareQuorumQueueWithDeadLettering()
    {
        var args = RabbitMqTopology.QueueArguments(new RabbitMqOptions { Queue = "notification-service.events", DeliveryLimit = 5 });

        args["x-queue-type"].ShouldBe("quorum");
        args["x-delivery-limit"].ShouldBe(5);
        args["x-dead-letter-exchange"].ShouldBe("fiapx.dlx");
        args["x-dead-letter-routing-key"].ShouldBe("notification-service.events.dlq");
    }

    [Fact]
    public void Bindings_MatchTheMessagingDocument() =>
        RabbitMqTopology.Bindings.ShouldBe(["user.registered", "video.processed", "video.processing-failed"]);

    [Fact]
    public void DefaultOptions_MatchTheMessagingDocument()
    {
        var options = new RabbitMqOptions();

        options.Queue.ShouldBe("notification-service.events");
        options.ConsumerName.ShouldBe("notification-service");
        options.DeliveryLimit.ShouldBe(5);
        options.ConsumerEnabled.ShouldBeTrue();
    }
}
