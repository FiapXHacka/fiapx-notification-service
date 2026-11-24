namespace FiapX.Notification.Domain;

/// <summary>
/// Local projection of a user, built from <c>user.registered</c> events (ADR-0012).
/// The notification service never calls the identity service to find an e-mail address.
/// </summary>
public sealed class UserContact
{
    private UserContact()
    {
        Email = string.Empty;
        Name = string.Empty;
    }

    public Guid UserId { get; private set; }
    public string Email { get; private set; }
    public string Name { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static UserContact Create(Guid userId, string email, string name, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        var contact = new UserContact { UserId = userId };
        contact.Update(email, name, now);
        return contact;
    }

    public void Update(string email, string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (email.IndexOf('@', StringComparison.Ordinal) < 1)
        {
            throw new ArgumentException($"'{email}' is not a valid e-mail address.", nameof(email));
        }

        Email = email.Trim();
        Name = name.Trim();
        UpdatedAt = now;
    }
}
