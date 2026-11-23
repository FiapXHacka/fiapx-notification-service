using FiapX.Notification.Application.Messaging;
using FiapX.Notification.Domain;
using FiapX.Notification.Infrastructure.Persistence;

namespace FiapX.Notification.IntegrationTests;

/// <summary>
/// Stand-in handler to exercise the consumer pipeline (inbox, transaction, nack → DLQ).
/// The real user.registered projection is issue #2.
/// </summary>
internal sealed class UserRegisteredTestHandler(NotificationDbContext db, TimeProvider time) : IIntegrationEventHandler
{
    public const string FailingName = "boom";

    public string EventType => "user.registered";

    public async Task HandleAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var data = integrationEvent.GetData<UserRegistered>();
        if (data.Name == FailingName)
        {
            throw new InvalidOperationException("simulated handler failure");
        }

        var contact = await db.UserContacts.FindAsync([data.UserId], cancellationToken);
        if (contact is null)
        {
            db.UserContacts.Add(UserContact.Create(data.UserId, data.Email, data.Name, time.GetUtcNow()));
        }
        else
        {
            contact.Update(data.Email, data.Name, time.GetUtcNow());
        }
    }

    internal sealed record UserRegistered(Guid UserId, string Email, string Name);
}
