using Crave.Analytics.Api.Modules.Analytics.Documents;
using Crave.Analytics.Api.Modules.Analytics.Dtos;
using MongoDB.Driver;

namespace Crave.Analytics.Api.Modules.Analytics.Repositories;

public sealed class RestaurantDayRepository(IMongoDatabase database)
{
    private readonly IMongoCollection<RestaurantDayDocument> _collection = database.GetCollection<RestaurantDayDocument>(RestaurantDayDocument.CollectionName);

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


    public async Task<List<RestaurantDayResponseDto>> GetRangeAsync(int restaurantId, string from, string to, CancellationToken cancellationToken)
    {
        var filter = Builders<RestaurantDayDocument>.Filter
            .Gte(day => day.Date, from) &
            Builders<RestaurantDayDocument>.Filter.Lte(day => day.Date, to) &
            Builders<RestaurantDayDocument>.Filter.Eq(day => day.RestaurantId, restaurantId);

        return await _collection.Find(filter)
            .SortBy(day => day.Date)
            .Project(day => new RestaurantDayResponseDto
            {
                Date = day.Date,
                OrdersCount = day.OrdersCount,
                RevenueMinor = day.RevenueSum,
                Currency = day.Currency,
            })
            .ToListAsync(cancellationToken);
    }
}
