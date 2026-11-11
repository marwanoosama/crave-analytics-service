using MongoDB.Bson;
using MongoDB.Driver;

namespace Crave.Analytics.Api.Health;

public static class HealthEndpoints
{
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(2);

    public static void MapHealthEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/health", async (IMongoDatabase database, CancellationToken requestAborted) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
            timeout.CancelAfter(PingTimeout);
            try
            {
                await database.RunCommandAsync<BsonDocument>(
                    new BsonDocument("ping", 1), cancellationToken: timeout.Token);
                return Results.Json(new { ok = true });
            }
            catch (Exception error) when (error is MongoException or OperationCanceledException)
            {
                return Results.Json(new { ok = false }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
}
