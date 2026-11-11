namespace Crave.Analytics.Common.Messaging;

public sealed record ConsumerOptions(
    string Url,
    string Exchange,
    string Queue,
    IReadOnlyList<string> Bindings,
    string DeadLetterExchange,
    string DeadLetterQueue,
    ushort Prefetch);
