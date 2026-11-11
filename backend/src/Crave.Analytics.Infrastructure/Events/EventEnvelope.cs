using System.Text.Json;

namespace Crave.Analytics.Infrastructure.Events;

public sealed record EventEnvelope(
    string EventId,
    string EventType,
    string OccurredAt,
    string AggregateType,
    string AggregateId,
    string Region,
    JsonElement Payload)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static bool TryParse(byte[] body, out EventEnvelope? envelope)
    {
        envelope = null;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope>(body, Json);
        }
        catch (JsonException)
        {
            return false;
        }

        return envelope is not null
            && envelope.EventId.Length > 0
            && envelope.EventType.Length > 0;
    }
}
