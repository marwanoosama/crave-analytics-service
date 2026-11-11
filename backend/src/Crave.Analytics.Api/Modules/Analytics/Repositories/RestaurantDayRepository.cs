using Crave.Analytics.Api.Modules.Analytics.Documents;
using MongoDB.Driver;

namespace Crave.Analytics.Api.Modules.Analytics.Repositories;

public sealed class RestaurantDayRepository(IMongoDatabase database)
{
    private readonly IMongoCollection<RestaurantDayDocument> _collection =
        database.GetCollection<RestaurantDayDocument>(RestaurantDayDocument.CollectionName);

    public async Task IncrementAsync(int restaurantId, string date, long subtotal, string currency, CancellationToken cancellationToken)
    {
        var filter = Builders<RestaurantDayDocument>.Filter
            .Where(day => day.RestaurantId == restaurantId && day.Date == date);
        var update = Builders<RestaurantDayDocument>.Update
            .Inc(day => day.OrdersCount, 1)
            .Inc(day => day.RevenueSum, subtotal)
            .Set(day => day.Currency, currency)
            .Set(day => day.UpdatedAt, DateTime.UtcNow);

        await _collection.FindOneAndUpdateAsync(filter, update,
            new FindOneAndUpdateOptions<RestaurantDayDocument> { IsUpsert = true }, cancellationToken);
    }
}
