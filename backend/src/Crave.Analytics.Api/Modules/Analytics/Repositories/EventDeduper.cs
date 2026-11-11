using Crave.Analytics.Api.Modules.Analytics.Documents;
using Crave.Analytics.Infrastructure.Events;
using MongoDB.Driver;

namespace Crave.Analytics.Api.Modules.Analytics.Repositories;

public sealed class EventDeduper(IMongoDatabase database) : IEventDeduper
{
    public async Task<bool> MarkSeen(string eventId, CancellationToken cancellationToken)
    {
        try
        {
            await database.GetCollection<EventIdDocument>(EventIdDocument.CollectionName)
                .InsertOneAsync(
                    new EventIdDocument { EventId = eventId, ReceivedAt = DateTime.UtcNow },
                    new InsertOneOptions(), cancellationToken);
            return true;
        }
        catch (MongoWriteException error) when (error.WriteError.Code == 11000)
        {
            return false;
        }
    }
}
