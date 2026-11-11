using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Crave.Analytics.Infrastructure.Http;

public sealed class ErrorHandlerMiddleware(ILogger<ErrorHandlerMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (AppException error)
        {
            Log(context, error);
            if (error.IsOperational)
                await Write(context, error.StatusCode, error.Message);
            else
                await Write(context, StatusCodes.Status500InternalServerError, "Something went wrong");
        }
        catch (Exception error)
        {
            Log(context, error);
            await Write(context, StatusCodes.Status500InternalServerError, "Something went wrong");
        }
    }

    private void Log(HttpContext context, Exception error)
    {
        context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var correlationId);
        logger.LogError(error, "{Message} ({Method} {Path}, correlationId={CorrelationId})",
            error.Message, context.Request.Method, context.Request.Path, correlationId);
    }

    private static async Task Write(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new { error = message });
    }
}
