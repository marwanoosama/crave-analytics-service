using Crave.Analytics.Infrastructure.Config;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Crave.Analytics.Infrastructure.Mongo;

public static class MongoServiceCollectionExtensions
{
    public static IServiceCollection AddMongo(this IServiceCollection services) =>
        services.AddSingleton<IMongoDatabase>(_ =>
            new MongoClient(Env.MongoUrl).GetDatabase(Env.MongoDatabase));
}
