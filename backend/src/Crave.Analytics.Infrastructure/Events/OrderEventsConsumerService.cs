using Crave.Analytics.Common.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crave.Analytics.Infrastructure.Events;

public sealed class OrderEventsConsumerService(
    AmqpConsumer consumer,
    ConsumerOptions options,
    IEventDeduper deduper,
    EventHandlerRegistry registry,
    ILogger<OrderEventsConsumerService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await consumer.StartAsync(options, OnMessage, stoppingToken);
                logger.LogInformation("Consumer started {Queue}, bindings {Bindings}",
                    options.Queue, string.Join(',', options.Bindings));
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Consumer connect failed, retrying in {Delay}s: {Message}",
                    RetryDelay.TotalSeconds, error.Message);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task OnMessage(byte[] body, Action ack, Action reject)
    {
        if (!EventEnvelope.TryParse(body, out var envelope))
        {
            logger.LogWarning("Unparseable envelope, dead-lettering (length {Length})", body.Length);
            reject();
            return;
        }

        if (!await deduper.MarkSeen(envelope.EventId, CancellationToken.None))
        {
            logger.LogInformation("Duplicate event ack-skipped {EventType} {EventId}",
                envelope.EventType, envelope.EventId);
            ack();
            return;
        }

        if (!registry.TryGet(envelope.EventType, out var handler))
        {
            logger.LogWarning("No handler for {EventType}, acking ({EventId})",
                envelope.EventType, envelope.EventId);
            ack();
            return;
        }

        try
        {
            await handler.HandleAsync(envelope.Payload, CancellationToken.None);
            ack();
        }
        catch (Exception error)
        {
            logger.LogError(error, "Handler {EventType} failed, dead-lettering ({EventId}): {Message}",
                envelope.EventType, envelope.EventId, error.Message);
            reject();
        }
    }
}
