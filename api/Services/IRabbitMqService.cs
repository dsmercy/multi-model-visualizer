using RabbitMQ.Client;

namespace MultiModelVisualizer.Api.Services;

/// <summary>
/// Publishes job IDs to RabbitMQ queues and provides a managed connection factory.
/// </summary>
public interface IRabbitMqService : IDisposable
{
    /// <summary>Publish a job ID to the specified queue. Creates queue if it doesn't exist.</summary>
    void Publish(string queueName, Guid jobId);

    /// <summary>Create a new channel for consuming from RabbitMQ (caller owns the channel).</summary>
    IModel CreateChannel();
}

public class RabbitMqService : IRabbitMqService
{
    private readonly IConnection _connection;
    private readonly IModel _publishChannel;
    private readonly object _lock = new();
    private readonly ILogger<RabbitMqService> _logger;

    private static readonly HashSet<string> _declaredQueues = new();

    // DLQ args applied to every work queue so nacked messages route to generation.dlq
    private static readonly Dictionary<string, object> DlqArgs = new()
    {
        { "x-dead-letter-exchange", "" },
        { "x-dead-letter-routing-key", "generation.dlq" },
    };

    public RabbitMqService(IConfiguration config, ILogger<RabbitMqService> logger)
    {
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = config["RabbitMQ:Host"] ?? "localhost",
            Port = config.GetValue("RabbitMQ:Port", 5672),
            UserName = config["RabbitMQ:Username"] ?? "dev",
            Password = config["RabbitMQ:Password"] ?? "dev",
            VirtualHost = config["RabbitMQ:VirtualHost"] ?? "/",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            DispatchConsumersAsync = true,
        };

        _connection = factory.CreateConnection("visuallearning-api");
        _publishChannel = _connection.CreateModel();

        // Pre-declare DLQ first, then all work queues with consistent args
        _publishChannel.QueueDeclare("generation.dlq", durable: true, exclusive: false, autoDelete: false);
        foreach (var q in new[] { "generation.diagram", "generation.text", "generation.fallback", "generation.3d", "generation.video" })
        {
            _publishChannel.QueueDeclare(q, durable: true, exclusive: false, autoDelete: false, arguments: DlqArgs);
            _declaredQueues.Add(q);
        }

        _logger.LogInformation("RabbitMQ connected to {Host}:{Port}", factory.HostName, factory.Port);
    }

    public void Publish(string queueName, Guid jobId)
    {
        lock (_lock)
        {
            EnsureQueue(queueName);
            var body = jobId.ToByteArray();
            _publishChannel.BasicPublish(exchange: "", routingKey: queueName, basicProperties: null, body: body);
            _logger.LogDebug("Published job {JobId} to queue {Queue}", jobId, queueName);
        }
    }

    public IModel CreateChannel()
    {
        return _connection.CreateModel();
    }

    private void EnsureQueue(string queueName)
    {
        if (_declaredQueues.Contains(queueName)) return;
        // Unknown queue — declare with DLQ args to stay consistent
        _publishChannel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false, arguments: DlqArgs);
        _declaredQueues.Add(queueName);
    }

    public void Dispose()
    {
        try { _publishChannel.Close(); } catch { }
        try { _connection.Close(); } catch { }
    }
}
