using System.Collections.Concurrent;
using FiapX.Notification.Application.Messaging;
using FiapX.Notification.Domain;
using FiapX.Notification.Infrastructure.Persistence;

namespace FiapX.Notification.IntegrationTests;

/// <summary>
/// Stand-in handler to exercise the consumer pipeline (inbox, retries, reject to the DLQ).
/// The real user.registered projection is issue #2. It records how many times each message reached it.
/// </summary>
internal sealed class UserRegisteredTestHandler(NotificationDbContext db, TimeProvider time) : IIntegrationEventHandler
{
    public const string FailingName = "boom";

    public static ConcurrentDictionary<Guid, int> Attempts { get; } = new();

    public string EventType => "user.registered";

    public async Task HandleAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        Attempts.AddOrUpdate(integrationEvent.Id, 1, (_, n) => n + 1);

        var data = integrationEvent.GetData<UserRegistered>();
        if (data.Name == FailingName)
        {
            throw new InvalidOperationException("simulated transient handler failure");
        }

        UserContact contact;
        try
        {
            contact = await db.UserContacts.FindAsync([data.UserId], cancellationToken)
                ?? UserContact.Create(data.UserId, data.Email, data.Name, time.GetUtcNow());
            contact.Update(data.Email, data.Name, time.GetUtcNow());
        }
        catch (ArgumentException ex)
        {
            // Validation failures of incoming data are permanent: the consumer rejects them to the DLQ.
            throw new InvalidMessageDataException($"user.registered {integrationEvent.Id} is invalid: {ex.Message}", ex);
        }

        if (db.Entry(contact).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
        {
            db.UserContacts.Add(contact);
        }
    }

    internal sealed record UserRegistered(Guid UserId, string Email, string Name);
}
