using FiapX.Notification.Domain;
using FiapX.Notification.Infrastructure.Persistence;
using Shouldly;

namespace FiapX.Notification.UnitTests.Persistence;

public class EnumNamesTests
{
    [Theory]
    [InlineData(NotificationType.VideoFailed, "VIDEO_FAILED")]
    [InlineData(NotificationType.VideoCompleted, "VIDEO_COMPLETED")]
    public void RoundTrip_UsesUpperSnakeCase(NotificationType value, string text)
    {
        EnumNames.ToDatabase(value).ShouldBe(text);
        EnumNames.FromDatabase<NotificationType>(text).ShouldBe(value);
    }

    [Fact]
    public void Converter_MatchesTheSchemaCheckConstraints()
    {
        var converter = EnumNames.Converter<NotificationStatus>();

        converter.ConvertToProviderExpression.Compile()(NotificationStatus.Pending).ShouldBe("PENDING");
        converter.ConvertFromProviderExpression.Compile()("FAILED").ShouldBe(NotificationStatus.Failed);
        EnumNames.ToDatabase(NotificationChannel.Email).ShouldBe("EMAIL");
    }
}
