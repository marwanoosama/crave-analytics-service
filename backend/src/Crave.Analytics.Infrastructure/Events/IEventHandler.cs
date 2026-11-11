using System.Text.Json;

namespace Crave.Analytics.Infrastructure.Events;

public interface IEventHandler
{
    string EventType { get; }

    Task HandleAsync(JsonElement payload, CancellationToken cancellationToken);
}
