using MongoDB.Driver;

namespace Crave.Analytics.Common.Mongo;

public static class IndexEnsurer
{
    // ensure index on this collection is created idempotently
    public static async Task EnsureAsync<T>(IMongoDatabase database, string collectionName, IReadOnlyList<CreateIndexModel<T>> models) 
        => await database.GetCollection<T>(collectionName).Indexes.CreateManyAsync(models);
}
