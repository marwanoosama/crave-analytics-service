namespace Crave.Analytics.Infrastructure.Events;

public sealed class EventHandlerRegistry(IEnumerable<IEventHandler> handlers)
{
    private readonly Dictionary<string, IEventHandler> _handlers =
        handlers.ToDictionary(handler => handler.EventType, StringComparer.Ordinal);

    public bool TryGet(string eventType, out IEventHandler handler) =>
        _handlers.TryGetValue(eventType, out handler!);
}
