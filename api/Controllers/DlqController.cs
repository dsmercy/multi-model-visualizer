using Microsoft.AspNetCore.Mvc;
using MultiModelVisualizer.Api.Models;
using MultiModelVisualizer.Api.Services;
using RabbitMQ.Client;

namespace MultiModelVisualizer.Api.Controllers;

[ApiController]
[Route("api/admin/dlq")]
public class DlqController : ControllerBase
{
    private readonly IRabbitMqService _rabbit;
    private readonly ILogger<DlqController> _logger;

    public DlqController(IRabbitMqService rabbit, ILogger<DlqController> logger)
    {
        _rabbit = rabbit;
        _logger = logger;
    }

    /// <summary>Peek up to 100 messages in the DLQ without removing them.</summary>
    [HttpGet]
    public IActionResult GetDlqMessages()
    {
        using var channel = _rabbit.CreateChannel();
        channel.QueueDeclare(QueueNames.Dlq, durable: true, exclusive: false, autoDelete: false);

        var messages = new List<object>();
        for (int i = 0; i < 100; i++)
        {
            var result = channel.BasicGet(QueueNames.Dlq, autoAck: false);
            if (result == null) break;

            Guid? jobId = null;
            if (result.Body.Length == 16)
                jobId = new Guid(result.Body.ToArray());

            messages.Add(new
            {
                deliveryTag = result.DeliveryTag,
                jobId,
                messageCount = result.MessageCount,
                redelivered = result.Redelivered,
            });

            // Nack back to DLQ (requeue=true so we're just peeking)
            channel.BasicNack(result.DeliveryTag, multiple: false, requeue: true);
        }

        return Ok(new { queue = QueueNames.Dlq, count = messages.Count, messages });
    }

    /// <summary>Reprocess a specific job from the DLQ by re-publishing to its original queue.</summary>
    [HttpPost("{jobId}/reprocess")]
    public IActionResult Reprocess(Guid jobId, [FromQuery] string? queue)
    {
        var targetQueue = queue ?? QueueNames.Diagram;
        if (!IsKnownQueue(targetQueue))
            return BadRequest(new { error = $"Unknown queue: {targetQueue}. Valid: {string.Join(", ", KnownQueues)}" });

        // Drain the DLQ looking for this job ID, requeue others
        using var channel = _rabbit.CreateChannel();
        channel.QueueDeclare(QueueNames.Dlq, durable: true, exclusive: false, autoDelete: false);

        bool found = false;
        for (int i = 0; i < 10000; i++)
        {
            var result = channel.BasicGet(QueueNames.Dlq, autoAck: false);
            if (result == null) break;

            Guid? msgJobId = null;
            if (result.Body.Length == 16)
                msgJobId = new Guid(result.Body.ToArray());

            if (msgJobId == jobId && !found)
            {
                // Ack from DLQ and re-publish to target queue
                channel.BasicAck(result.DeliveryTag, multiple: false);
                _rabbit.Publish(targetQueue, jobId);
                found = true;
                _logger.LogInformation("Reprocessed job {JobId} from DLQ to queue {Queue}", jobId, targetQueue);
            }
            else
            {
                // Put back
                channel.BasicNack(result.DeliveryTag, multiple: false, requeue: true);
            }
        }

        if (!found)
            return NotFound(new { error = $"Job {jobId} not found in DLQ" });

        return Ok(new { jobId, reprocessedTo = targetQueue });
    }

    /// <summary>Purge all messages from the DLQ.</summary>
    [HttpDelete]
    public IActionResult PurgeDlq()
    {
        using var channel = _rabbit.CreateChannel();
        channel.QueueDeclare(QueueNames.Dlq, durable: true, exclusive: false, autoDelete: false);
        var count = channel.QueuePurge(QueueNames.Dlq);
        _logger.LogWarning("DLQ purged: {Count} messages deleted", count);
        return Ok(new { purged = count });
    }

    private static readonly string[] KnownQueues = { QueueNames.Diagram, QueueNames.Text, QueueNames.Fallback, QueueNames.ThreeD, QueueNames.Video };
    private static bool IsKnownQueue(string q) => Array.IndexOf(KnownQueues, q) >= 0;
}
