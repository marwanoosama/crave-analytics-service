using System.Globalization;
using System.Text.Json;
using Crave.Analytics.Api.Modules.Analytics.Repositories;
using Crave.Analytics.Infrastructure.Events;
using Crave.Analytics.Infrastructure.Http;

namespace Crave.Analytics.Api.Modules.Analytics.Services;


public sealed class OrderPlacedHandler(RestaurantDayRepository repository) : IEventHandler
{
    public string EventType => "order.placed";

    public async Task HandleAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var order = Parse(payload);
        await repository.IncrementAsync(order.RestaurantId, order.Date, order.Subtotal, order.Currency, cancellationToken);
    }

    private sealed record Order(int RestaurantId, long Subtotal, string Currency, string Date);

    private static Order Parse(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new AppException("order.placed payload must be a JSON object");

        var placedAt = Text(payload, "placedAt");
        if (!DateTime.TryParse(placedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var placed))
            throw new AppException("order.placed payload has invalid placedAt");

        var restaurantId = Number(payload, "restaurantId").GetInt32();
        var subtotal = Number(payload, "subtotal").GetInt64();
        if (restaurantId <= 0)
            throw new AppException("order.placed payload has invalid restaurantId");
        if (subtotal < 0)
            throw new AppException("order.placed payload has negative subtotal");

        var currency = Text(payload, "currency");

        return new Order(restaurantId, subtotal, currency,
            placed.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static JsonElement Number(JsonElement payload, string field)
    {
        if (payload.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number)
            return value;
        throw new AppException($"order.placed payload is missing numeric {field}");
    }

    private static string Text(JsonElement payload, string field)
    {
        var value = payload.TryGetProperty(field, out var property) &&
                    property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
        if (string.IsNullOrEmpty(value))
            throw new AppException($"order.placed payload is missing string {field}");
        return value;
    }
}
