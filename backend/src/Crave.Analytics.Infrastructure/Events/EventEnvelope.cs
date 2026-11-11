using System.Diagnostics.CodeAnalysis;
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

    public static bool TryParse(byte[] body, [NotNullWhen(true)] out EventEnvelope? envelope)
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
            && !string.IsNullOrEmpty(envelope.EventId)
            && !string.IsNullOrEmpty(envelope.EventType);
    }
}
