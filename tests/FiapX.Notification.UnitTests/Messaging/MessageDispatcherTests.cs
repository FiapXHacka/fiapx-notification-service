using System.Text;
using FiapX.Notification.Application.Messaging;
using FiapX.Notification.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace FiapX.Notification.UnitTests.Messaging;

/// <summary>Paths that end before touching the database. The transactional path is covered by integration tests.</summary>
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

    private static ReadOnlyMemory<byte> Envelope(string type, string id = "01936f5e-8a4b-7c3d-9e2f-1a2b3c4d5e6f") =>
        Encoding.UTF8.GetBytes($$$"""
            {"specversion":"1.0","id":"{{{id}}}","source":"test","type":"{{{type}}}","time":"2026-11-23T18:00:00Z",
             "subject":"s","dataschema":"fiapx/{{{type}}}/v1","data":{}}
            """);

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"specversion":"1.0","id":"00000000-0000-0000-0000-000000000000","source":"s","type":"user.registered","time":"2026-11-23T18:00:00Z","subject":"s","dataschema":"d","data":{}}""")]
    [InlineData("""{"specversion":"1.0","id":"01936f5e-8a4b-7c3d-9e2f-1a2b3c4d5e6f","source":"s","type":" ","time":"2026-11-23T18:00:00Z","subject":"s","dataschema":"d","data":{}}""")]
    public async Task Malformed_IsAckedWithoutCallingHandlers(string body)
    {
        var outcome = await CreateDispatcher().DispatchAsync(Encoding.UTF8.GetBytes(body), CancellationToken.None);

        outcome.ShouldBe(DispatchOutcome.Malformed);
    }

    [Fact]
    public async Task UnsubscribedType_IsIgnored()
    {
        var outcome = await CreateDispatcher().DispatchAsync(Envelope("video.uploaded"), CancellationToken.None);

        outcome.ShouldBe(DispatchOutcome.Ignored);
    }

    [Theory]
    [InlineData("user.registered")]
    [InlineData("video.processed")]
    [InlineData("video.processing-failed")]
    public async Task SubscribedTypeWithoutHandler_ThrowsSoTheMessageIsNotLost(string type)
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            CreateDispatcher().DispatchAsync(Envelope(type), CancellationToken.None));

        ex.Message.ShouldContain(type);
    }
}
