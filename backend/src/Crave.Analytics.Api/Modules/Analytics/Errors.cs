using Crave.Analytics.Infrastructure.Http;

namespace Crave.Analytics.Api.Modules.Analytics;

/// <summary>
/// Stable error instances (the siblings' Errors.ts convention) — messages are
/// the api-contracts.md §1.1 error table.
/// </summary>
public static class Errors
{
    public static readonly AppException FromToRequired =
        new("from and to are required (YYYY-MM-DD)", 400);

    public static readonly AppException InvertedRange =
        new("from must be before or equal to to", 400);

    public static readonly AppException RangeTooWide =
        new("date range must not exceed 366 days", 400);
}
