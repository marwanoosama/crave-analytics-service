namespace Crave.Analytics.Infrastructure.Events;

public interface IEventDeduper
{
    Task<bool> MarkSeen(string eventId, CancellationToken cancellationToken);
}
