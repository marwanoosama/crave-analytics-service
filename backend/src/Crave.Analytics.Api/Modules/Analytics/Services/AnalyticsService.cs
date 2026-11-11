using System.Globalization;
using Crave.Analytics.Api.Modules.Analytics.Dtos;
using Crave.Analytics.Api.Modules.Analytics.Repositories;
using Crave.Analytics.Infrastructure.Http;

namespace Crave.Analytics.Api.Modules.Analytics.Services;

public sealed class AnalyticsService(RestaurantDayRepository repository)
{
    private const int MaxRangeDays = 366;

    public async Task<IReadOnlyList<RestaurantDayResponseDto>> GetDaysAsync(RestaurantDaysRequestDto request, CancellationToken cancellationToken)
    {
        var (start, end) = ValidateRange(request.From, request.To);
        var rows = await repository.GetRangeAsync(request.RestaurantId, start, end, cancellationToken);

        foreach (var row in rows)
            row.AvgOrderMinor = row.RevenueMinor / row.OrdersCount; 
        return rows;
    }

    private static (string From, string To) ValidateRange(string? from, string? to)
    {
        if (!DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start)
            || !DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var end))
            throw Errors.FromToRequired;

        if (start > end)
            throw Errors.InvertedRange;
        if ((end - start).TotalDays >= MaxRangeDays)
            throw Errors.RangeTooWide;

        return (start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
