using FiapX.Notification.Domain;
using Shouldly;

namespace FiapX.Notification.UnitTests.Domain;

public class UserContactTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_TrimsValuesAndStampsTime()
    {
        var id = Guid.CreateVersion7();

        var contact = UserContact.Create(id, "  ana@fiapx.dev ", " Ana ", Now);

        contact.UserId.ShouldBe(id);
        contact.Email.ShouldBe("ana@fiapx.dev");
        contact.Name.ShouldBe("Ana");
        contact.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Update_ReplacesEmailAndName()
    {
        var contact = UserContact.Create(Guid.CreateVersion7(), "ana@fiapx.dev", "Ana", Now);

        contact.Update("ana.souza@fiapx.dev", "Ana Souza", Now.AddHours(1));

        contact.Email.ShouldBe("ana.souza@fiapx.dev");
        contact.Name.ShouldBe("Ana Souza");
        contact.UpdatedAt.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void Create_RejectsEmptyUserId() =>
        Should.Throw<ArgumentException>(() => UserContact.Create(Guid.Empty, "ana@fiapx.dev", "Ana", Now));

    [Theory]
    [InlineData("", "Ana")]
    [InlineData("ana@fiapx.dev", " ")]
    [InlineData("not-an-email", "Ana")]
    [InlineData("@fiapx.dev", "Ana")]
    public void Create_RejectsInvalidData(string email, string name) =>
        Should.Throw<ArgumentException>(() => UserContact.Create(Guid.CreateVersion7(), email, name, Now));
}
