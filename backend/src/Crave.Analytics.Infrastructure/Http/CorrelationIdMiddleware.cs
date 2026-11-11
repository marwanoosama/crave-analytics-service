using Microsoft.AspNetCore.Http;

namespace Crave.Analytics.Infrastructure.Http;

public sealed class CorrelationIdMiddleware : IMiddleware
{
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var incoming = context.Request.Headers["X-CorrelationId"].FirstOrDefault()
                    ?? context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        var correlationId = string.IsNullOrEmpty(incoming) ? Guid.NewGuid().ToString() : incoming;

        context.Items[ItemKey] = correlationId;
        context.Response.Headers["X-CorrelationId"] = correlationId;

        await next(context);
    }
}
