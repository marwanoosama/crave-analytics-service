using Crave.Analytics.Api.Modules.Analytics.Documents;
using Crave.Analytics.Common.Mongo;
using Crave.Analytics.Infrastructure.Config;
using MongoDB.Driver;

namespace Crave.Analytics.Api.Modules.Analytics.Repositories;

// creating the indexes
public static class Indexes
{
    public static async Task EnsureAsync(IMongoDatabase database)
    {
        await IndexEnsurer.EnsureAsync(database, RestaurantDayDocument.CollectionName,
        [
            // upsert identity
            new CreateIndexModel<RestaurantDayDocument>(
                Builders<RestaurantDayDocument>.IndexKeys
                    .Ascending(x => x.RestaurantId)
                    .Ascending(x => x.Date),
                new CreateIndexOptions { Unique = true }),

            // for date scan
            new CreateIndexModel<RestaurantDayDocument>(
                Builders<RestaurantDayDocument>.IndexKeys
                    .Ascending(x => x.Date)
                    .Ascending(x => x.RestaurantId)),
        ]);

        await IndexEnsurer.EnsureAsync(database, EventIdDocument.CollectionName,
        [
            new CreateIndexModel<EventIdDocument>(
                Builders<EventIdDocument>.IndexKeys.Ascending(x => x.EventId),
                new CreateIndexOptions { Unique = true }),

            // index by data or cleanup
            new CreateIndexModel<EventIdDocument>(
                Builders<EventIdDocument>.IndexKeys.Ascending(x => x.ReceivedAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(Env.EventDedupeTtlDays) }),
        ]);
    }
}
