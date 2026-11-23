using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiapX.Notification.Application.Messaging;

/// <summary>
/// CloudEvents 1.0 envelope in structured JSON mode (ADR-0006, fiapx/docs/contracts/asyncapi.yaml).
/// </summary>
public sealed record CloudEvent<TData>
{
    [JsonPropertyName("specversion")]
    public string SpecVersion { get; init; } = "1.0";

    /// <summary>Message id: the idempotency key used by the inbox.</summary>
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    /// <summary>Event type, also the RabbitMQ routing key (e.g. <c>user.registered</c>).</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("time")]
    public required DateTimeOffset Time { get; init; }

    /// <summary>Aggregate id (videoId or userId).</summary>
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("dataschema")]
    public required string DataSchema { get; init; }

    [JsonPropertyName("datacontenttype")]
    public string DataContentType { get; init; } = "application/json";

    /// <summary>W3C trace context, so the trace continues across the broker (ADR-0015).</summary>
    [JsonPropertyName("traceparent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceParent { get; init; }

    [JsonPropertyName("data")]
    public required TData Data { get; init; }
}

/// <summary>A received event whose data is still raw JSON; handlers deserialize it to their own type.</summary>
public sealed record IntegrationEvent(CloudEvent<JsonElement> Envelope)
{
    public Guid Id => Envelope.Id;
    public string Type => Envelope.Type;

    public TData GetData<TData>() =>
        Envelope.Data.Deserialize<TData>(CloudEventJson.Options)
        ?? throw new JsonException($"Event {Id} ({Type}) has empty data.");
}

public static class CloudEventJson
{
    /// <summary>camelCase, as in the AsyncAPI contract; envelope attributes use explicit names.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
