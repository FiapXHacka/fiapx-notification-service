using System.Text.Json;
using FiapX.Notification.Application.Messaging;
using Shouldly;

namespace FiapX.Notification.UnitTests.Messaging;

public class CloudEventTests
{
    // Example of video.processing-failed as described in fiapx/docs/contracts/asyncapi.yaml.
    private const string ProcessingFailedExample = """
        {
          "specversion": "1.0",
          "id": "01936f5e-8a4b-7c3d-9e2f-1a2b3c4d5e6f",
          "source": "fiapx/video-processor",
          "type": "video.processing-failed",
          "time": "2026-11-23T18:04:05Z",
          "subject": "01936f5e-7f00-7aaa-8bbb-ccccdddd0001",
          "dataschema": "fiapx/video.processing-failed/v1",
          "datacontenttype": "application/json",
          "traceparent": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
          "data": {
            "videoId": "01936f5e-7f00-7aaa-8bbb-ccccdddd0001",
            "userId": "01936f50-0000-7000-8000-000000000042",
            "errorCode": "INVALID_VIDEO",
            "errorMessage": "o arquivo enviado não é um vídeo válido",
            "attempt": 1
          }
        }
        """;

    private sealed record ProcessingFailedData(Guid VideoId, Guid UserId, string ErrorCode, string ErrorMessage, int Attempt);

    [Fact]
    public void Deserialize_ContractExample()
    {
        var envelope = JsonSerializer.Deserialize<CloudEvent<JsonElement>>(ProcessingFailedExample, CloudEventJson.Options)!;

        envelope.SpecVersion.ShouldBe("1.0");
        envelope.Id.ShouldBe(Guid.Parse("01936f5e-8a4b-7c3d-9e2f-1a2b3c4d5e6f"));
        envelope.Source.ShouldBe("fiapx/video-processor");
        envelope.Type.ShouldBe("video.processing-failed");
        envelope.Time.ShouldBe(new DateTimeOffset(2026, 11, 23, 18, 4, 5, TimeSpan.Zero));
        envelope.DataSchema.ShouldBe("fiapx/video.processing-failed/v1");
        envelope.TraceParent.ShouldStartWith("00-4bf92f35");

        var data = new IntegrationEvent(envelope).GetData<ProcessingFailedData>();
        data.ErrorCode.ShouldBe("INVALID_VIDEO");
        data.UserId.ShouldBe(Guid.Parse("01936f50-0000-7000-8000-000000000042"));
        data.Attempt.ShouldBe(1);
    }

    [Fact]
    public void Serialize_UsesCloudEventsAttributeNames()
    {
        var envelope = new CloudEvent<object>
        {
            Id = Guid.Parse("01936f5e-8a4b-7c3d-9e2f-1a2b3c4d5e6f"),
            Source = "fiapx/notification-service",
            Type = "notification.sent",
            Time = DateTimeOffset.UnixEpoch,
            Subject = "s",
            DataSchema = "fiapx/notification.sent/v1",
            Data = new { videoId = "v" },
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(envelope, CloudEventJson.Options));
        var root = json.RootElement;

        root.GetProperty("specversion").GetString().ShouldBe("1.0");
        root.GetProperty("dataschema").GetString().ShouldBe("fiapx/notification.sent/v1");
        root.GetProperty("datacontenttype").GetString().ShouldBe("application/json");
        root.TryGetProperty("traceparent", out _).ShouldBeFalse("null traceparent must be omitted");
        root.GetProperty("data").GetProperty("videoId").GetString().ShouldBe("v");
    }

    [Fact]
    public void GetData_ThrowsOnNullData()
    {
        var envelope = JsonSerializer.Deserialize<CloudEvent<JsonElement>>(
            ProcessingFailedExample.Replace("\"data\": {", "\"data\": null, \"ignored\": {", StringComparison.Ordinal),
            CloudEventJson.Options)!;

        Should.Throw<JsonException>(() => new IntegrationEvent(envelope).GetData<ProcessingFailedData>());
    }
}
