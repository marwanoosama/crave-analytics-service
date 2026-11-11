using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Crave.Analytics.Api.Modules.Analytics.Documents;

public class EventIdDocument
{
    public const string CollectionName = "event_ids";

    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("event_id")]
    public string EventId { get; set; } = "";

    [BsonElement("received_at")]
    public DateTime ReceivedAt { get; set; }
}
