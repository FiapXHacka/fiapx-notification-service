using System.Text;
using FiapX.Notification.Application.Messaging;
using FiapX.Notification.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace FiapX.Notification.UnitTests.Messaging;

/// <summary>Paths that end before touching the database. The transactional paths are covered by integration tests.</summary>
public class MessageDispatcherTests
{
    private static MessageDispatcher CreateDispatcher(params IIntegrationEventHandler[] handlers)
    {
        var services = new ServiceCollection();
        foreach (var handler in handlers)
        {
            services.AddSingleton(handler);
        }

        return new MessageDispatcher(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new RabbitMqOptions()),
            TimeProvider.System,
            NullLogger<MessageDispatcher>.Instance);
    }

    private static IIntegrationEventHandler Handler(string eventType)
    {
        var handler = Substitute.For<IIntegrationEventHandler>();
        handler.EventType.Returns(eventType);
        return handler;
    }

    private static ReadOnlyMemory<byte> Envelope(string type) =>
        Encoding.UTF8.GetBytes($$$"""
            {"specversion":"1.0","id":"01936f5e-8a4b-7c3d-9e2f-1a2b3c4d5e6f","source":"test","type":"{{{type}}}",
             "time":"2026-11-23T18:00:00Z","subject":"s","dataschema":"fiapx/{{{type}}}/v1","data":{}}
            """);

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"specversion":"1.0","id":"00000000-0000-0000-0000-000000000000","source":"s","type":"user.registered","time":"2026-11-23T18:00:00Z","subject":"s","dataschema":"d","data":{}}""")]
    [InlineData("""{"specversion":"1.0","id":"01936f5e-8a4b-7c3d-9e2f-1a2b3c4d5e6f","source":"s","type":" ","time":"2026-11-23T18:00:00Z","subject":"s","dataschema":"d","data":{}}""")]
    public async Task EnvelopeWithoutIdOrType_IsIgnored(string body)
    {
        var handler = Handler("user.registered");

        var outcome = await CreateDispatcher(handler).DispatchAsync(Encoding.UTF8.GetBytes(body), CancellationToken.None);

        outcome.ShouldBe(DispatchOutcome.Ignored);
        await handler.DidNotReceiveWithAnyArgs().HandleAsync(default!, default);
    }

    [Theory]
    [InlineData("video.uploaded")]
    [InlineData("video.processed")]
    public async Task TypeNotHandledByThisService_IsIgnored(string type)
    {
        var outcome = await CreateDispatcher(Handler("user.registered")).DispatchAsync(Envelope(type), CancellationToken.None);

        outcome.ShouldBe(DispatchOutcome.Ignored);
    }

    [Fact]
    public void HasHandlers_ReflectsRegistrations()
    {
        CreateDispatcher().HasHandlers().ShouldBeFalse();
        CreateDispatcher(Handler("user.registered")).HasHandlers().ShouldBeTrue();
    }

    [Theory]
    [InlineData(1, 1.0, 200)]
    [InlineData(2, 1.0, 400)]
    [InlineData(3, 1.0, 800)]
    [InlineData(2, 0.8, 320)]
    [InlineData(2, 1.2, 480)]
    public void RetryDelay_IsExponentialWithJitter(int attempt, double jitter, int expectedMs) =>
        MessageDispatcher.RetryDelay(TimeSpan.FromMilliseconds(200), attempt, jitter)
            .ShouldBe(TimeSpan.FromMilliseconds(expectedMs));

    [Fact]
    public void RetryDelay_JitterStaysWithinTwentyPercent()
    {
        for (var i = 0; i < 50; i++)
        {
            var delay = MessageDispatcher.RetryDelay(TimeSpan.FromMilliseconds(200), 1);
            delay.ShouldBeInRange(TimeSpan.FromMilliseconds(160), TimeSpan.FromMilliseconds(240));
        }
    }
}
