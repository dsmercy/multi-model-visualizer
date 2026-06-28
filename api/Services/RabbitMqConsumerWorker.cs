using System.Threading.Channels;
using MultiModelVisualizer.Api.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MultiModelVisualizer.Api.Services;

/// <summary>
/// Consumes job IDs from all RabbitMQ queues and forwards them to the in-process channel.
/// Nacked messages are routed to generation.dlq via dead-letter exchange.
/// Cancelled job IDs are acked-and-skipped without processing.
/// </summary>
public class RabbitMqConsumerWorker : BackgroundService
{
    private readonly IRabbitMqService _rabbit;
    private readonly Channel<Guid> _localQueue;
    private readonly CancelledJobRegistry _cancelledJobs;
    private readonly ILogger<RabbitMqConsumerWorker> _logger;

    private static readonly string[] ConsumedQueues =
    {
        QueueNames.Diagram,
        QueueNames.Text,
        QueueNames.Fallback,
        QueueNames.ThreeD,
        QueueNames.Video,
    };

    public RabbitMqConsumerWorker(
        IRabbitMqService rabbit,
        Channel<Guid> localQueue,
        CancelledJobRegistry cancelledJobs,
        ILogger<RabbitMqConsumerWorker> logger)
    {
        _rabbit = rabbit;
        _localQueue = localQueue;
        _cancelledJobs = cancelledJobs;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RabbitMqConsumerWorker starting, subscribing to {Count} queues", ConsumedQueues.Length);

        var channel = _rabbit.CreateChannel();
        channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        // Declare DLQ first so it exists before main queues reference it
        channel.QueueDeclare(
            queue: QueueNames.Dlq,
            durable: true, exclusive: false, autoDelete: false,
            arguments: null);

        // Dead-letter exchange: default exchange ("") routes by routing key = queue name
        var dlqArgs = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", "" },
            { "x-dead-letter-routing-key", QueueNames.Dlq },
        };

        foreach (var queueName in ConsumedQueues)
        {
            channel.QueueDeclare(
                queue: queueName,
                durable: true, exclusive: false, autoDelete: false,
                arguments: dlqArgs);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.Received += async (_, ea) =>
            {
                var jobId = new Guid(ea.Body.ToArray());

                // Skip cancelled jobs — ack so they leave the queue cleanly
                if (_cancelledJobs.IsCancel(jobId))
                {
                    _logger.LogInformation("Skipping cancelled job {JobId} from queue {Queue}", jobId, queueName);
                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                    return;
                }

                try
                {
                    _logger.LogInformation("RabbitMQ received job {JobId} from queue {Queue}", jobId, queueName);
                    await _localQueue.Writer.WriteAsync(jobId, stoppingToken);
                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error forwarding job {JobId} from {Queue} — routing to DLQ", jobId, queueName);
                    // requeue=false → dead-letter exchange routes to generation.dlq
                    channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
            _logger.LogInformation("Subscribed to RabbitMQ queue: {Queue} (DLQ → {Dlq})", queueName, QueueNames.Dlq);
        }

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }

        try { channel.Close(); } catch { }
        _logger.LogInformation("RabbitMqConsumerWorker stopped.");
    }
}

/// <summary>Singleton registry of job IDs that should be skipped when dequeued from RabbitMQ.</summary>
public class CancelledJobRegistry
{
    private readonly HashSet<Guid> _ids = new();
    private readonly object _lock = new();

    public void Add(Guid jobId) { lock (_lock) _ids.Add(jobId); }
    public bool IsCancel(Guid jobId) { lock (_lock) return _ids.Contains(jobId); }
    public void Remove(Guid jobId) { lock (_lock) _ids.Remove(jobId); }
}
