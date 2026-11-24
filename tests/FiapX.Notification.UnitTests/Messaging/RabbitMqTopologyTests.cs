using System.ComponentModel.DataAnnotations;
using FiapX.Notification.Infrastructure.Messaging;
using Shouldly;

namespace FiapX.Notification.UnitTests.Messaging;

/// <summary>The topology must match fiapx local/rabbitmq/definitions.json argument by argument.</summary>
public class RabbitMqTopologyTests
{
    [Fact]
    public void QueueArguments_MatchTheDefinitions()
    {
        var args = RabbitMqTopology.QueueArguments(new RabbitMqOptions());

        args.Count.ShouldBe(4);
        args["x-queue-type"].ShouldBe("quorum");
        args["x-delivery-limit"].ShouldBe(5);
        args["x-dead-letter-exchange"].ShouldBe("fiapx.dlx");
        args["x-dead-letter-routing-key"].ShouldBe("notification-service.events.dlq");
    }

    [Fact]
    public void QueueNamesAndBindings_MatchTheMessagingDocument()
    {
        RabbitMqTopology.Queue.ShouldBe("notification-service.events");
        RabbitMqTopology.DeadLetterQueue.ShouldBe("notification-service.events.dlq");
        RabbitMqTopology.Bindings.ShouldBe(["user.registered", "video.processed", "video.processing-failed"]);
    }

    [Fact]
    public void DefaultOptions_AreTheCanonicalValues()
    {
        var options = new RabbitMqOptions();

        options.ClientName.ShouldBe("notification-service");
        options.Exchange.ShouldBe("fiapx.events");
        options.DeadLetterExchange.ShouldBe("fiapx.dlx");
        options.PrefetchCount.ShouldBe((ushort)16);
        options.ConsumerConcurrency.ShouldBe((ushort)4);
        options.MaxHandlerAttempts.ShouldBe(3);
        options.HandlerRetryDelay.ShouldBe(TimeSpan.FromMilliseconds(200));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6000)]
    public void Options_RejectOutOfRangeRetryDelay(int milliseconds)
    {
        var options = new RabbitMqOptions { HandlerRetryDelay = TimeSpan.FromMilliseconds(milliseconds) };

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true).ShouldBeFalse();
        results.ShouldContain(r => r.MemberNames.Contains(nameof(RabbitMqOptions.HandlerRetryDelay)));
    }
}
