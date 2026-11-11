using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Crave.Analytics.Common.Messaging;

public sealed class AmqpConsumer
{
    public delegate Task MessageReceived(byte[] body, Action ack, Action reject);

    public async Task StartAsync(ConsumerOptions options, MessageReceived onMessage, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(options.Url),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
        };

        var connection = await factory.CreateConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await AssertTopologyAsync(channel, options, cancellationToken);
        await channel.BasicQosAsync(0, options.Prefetch, false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            Action ack = () => { _ = channel.BasicAckAsync(delivery.DeliveryTag, false); };
            Action reject = () => { _ = channel.BasicNackAsync(delivery.DeliveryTag, false, requeue: false); };

            async Task Dispatch()
            {
                try
                {
                    await onMessage(delivery.Body.ToArray(), ack, reject);
                }
                catch
                {
                    reject(); 
                }
            }

            return Dispatch();
        };

        await channel.BasicConsumeAsync(options.Queue, autoAck: false, consumer, cancellationToken);
    }

    private static async Task AssertTopologyAsync(IChannel channel, ConsumerOptions options, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);

        if (options.DeadLetterExchange.Length > 0 && options.DeadLetterQueue.Length > 0)
        {
            await channel.ExchangeDeclareAsync(options.DeadLetterExchange, ExchangeType.Topic,
                durable: true, cancellationToken: cancellationToken);
            await channel.QueueDeclareAsync(options.DeadLetterQueue, durable: true,
                exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(options.DeadLetterQueue, options.DeadLetterExchange,
                "#", cancellationToken: cancellationToken);
        }

        var queueArguments = options.DeadLetterExchange.Length > 0
            ? new Dictionary<string, object?> { ["x-dead-letter-exchange"] = options.DeadLetterExchange } : null;

        await channel.QueueDeclareAsync(options.Queue, durable: true,
            exclusive: false, autoDelete: false, arguments: queueArguments,
            cancellationToken: cancellationToken);

        foreach (var binding in options.Bindings)
            await channel.QueueBindAsync(options.Queue, options.Exchange, binding,
                cancellationToken: cancellationToken);
    }
}
