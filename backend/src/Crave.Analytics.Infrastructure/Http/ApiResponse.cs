using Microsoft.AspNetCore.Http;

namespace Crave.Analytics.Infrastructure.Http;

public static class ApiResponse
{
    public static IResult SendSuccess<T>(T data, int statusCode = StatusCodes.Status200OK) =>
        Results.Json(new { success = true, data }, statusCode: statusCode);
}
