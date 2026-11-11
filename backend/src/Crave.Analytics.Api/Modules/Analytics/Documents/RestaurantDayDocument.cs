using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Crave.Analytics.Api.Modules.Analytics.Documents;

public class RestaurantDayDocument
{
    public const string CollectionName = "agg_restaurant_day";

    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("restaurant_id")]
    public int RestaurantId { get; set; }

    [BsonElement("date")]
    public string Date { get; set; } = "";

    [BsonElement("currency")]
    public string Currency { get; set; } = "";

    [BsonElement("orders_count")]
    public long OrdersCount { get; set; }

    [BsonElement("revenue_sum")]
    public long RevenueSum { get; set; }

    [BsonElement("delivery_ms_sum")]
    public long DeliveryMsSum { get; set; }

    [BsonElement("delivery_ms_count")]
    public long DeliveryMsCount { get; set; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
