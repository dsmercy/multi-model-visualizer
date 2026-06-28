using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MultiModelVisualizer.Api.Data;
using MultiModelVisualizer.Api.Models;

namespace MultiModelVisualizer.Api.Services;

/// <summary>
/// Non-blocking async review that runs after a job completes.
/// Classifies output quality: Minor → attach warning, Major → re-queue, Critical → FallbackGeneration.
/// </summary>
public class ReviewService : IReviewService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ReviewService> _logger;

    private int TextMinimumLength => _config.GetValue("Validation:TextMinimumLength", 200);

    public ReviewService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<ReviewService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    public async Task ReviewAsync(Guid jobId, string outputType, string? outputContent, CancellationToken ct = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var job = await db.GenerationJobs
                .Include(j => j.Session)
                .FirstOrDefaultAsync(j => j.JobId == jobId, ct);

            if (job?.Session == null) return;

            var (severity, notes) = ClassifyOutput(outputType, outputContent);

            job.ReviewStatus = "Reviewed";
            job.ReviewSeverity = severity;
            job.ReviewNotes = notes;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            db.GenerationJobs.Update(job);

            var session = job.Session;

            if (severity == null)
            {
                // Clean — no action needed, already Completed
                _logger.LogInformation("Job {JobId} review passed", jobId);
            }
            else if (severity == ReviewSeverity.Minor)
            {
                // Attach warning, stay Completed
                db.LearningSessionEvents.Add(new LearningSessionEvent
                {
                    SessionId = session.SessionId,
                    PreviousState = session.CurrentState,
                    NewState = WorkflowState.Reviewed,
                    Trigger = "ReviewMinor",
                    EventPayload = JsonSerializer.Serialize(new { severity, notes })
                });
                _logger.LogWarning("Job {JobId} minor review issue: {Notes}", jobId, notes);
            }
            else if (severity == ReviewSeverity.Major)
            {
                // Re-queue for regeneration
                db.LearningSessionEvents.Add(new LearningSessionEvent
                {
                    SessionId = session.SessionId,
                    PreviousState = session.CurrentState,
                    NewState = WorkflowState.GenerationQueued,
                    Trigger = "ReviewMajorRequeue",
                    EventPayload = JsonSerializer.Serialize(new { severity, notes })
                });
                session.CurrentState = WorkflowState.GenerationQueued;
                session.UpdatedDate = DateTimeOffset.UtcNow;

                var newJob = new GenerationJob
                {
                    SessionId = session.SessionId,
                    VisualizationType = job.VisualizationType,
                    Status = JobStatus.Queued,
                    QueueName = job.QueueName,
                };
                db.GenerationJobs.Add(newJob);
                await db.SaveChangesAsync(ct);

                // Re-enqueue via the job queue channel
                var queue = scope.ServiceProvider.GetRequiredService<System.Threading.Channels.Channel<Guid>>();
                await queue.Writer.WriteAsync(newJob.JobId, ct);

                _logger.LogWarning("Job {JobId} major review issue — re-queued as {NewJobId}: {Notes}", jobId, newJob.JobId, notes);
                return;
            }
            else if (severity == ReviewSeverity.Critical)
            {
                // Move to FallbackGeneration
                db.LearningSessionEvents.Add(new LearningSessionEvent
                {
                    SessionId = session.SessionId,
                    PreviousState = session.CurrentState,
                    NewState = WorkflowState.FallbackGeneration,
                    Trigger = "ReviewCritical",
                    EventPayload = JsonSerializer.Serialize(new { severity, notes })
                });
                session.CurrentState = WorkflowState.FallbackGeneration;
                session.UpdatedDate = DateTimeOffset.UtcNow;
                _logger.LogError("Job {JobId} critical review issue: {Notes}", jobId, notes);
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Review failed for job {JobId}", jobId);
        }
    }

    private (string? severity, string? notes) ClassifyOutput(string outputType, string? content)
    {
        if (string.IsNullOrWhiteSpace(content) && outputType != "url_only")
            return (ReviewSeverity.Critical, "Output content is empty");

        if (outputType == "text")
        {
            var len = content?.Length ?? 0;
            if (len < 50) return (ReviewSeverity.Critical, $"Text output critically short ({len} chars)");
            if (len < TextMinimumLength) return (ReviewSeverity.Minor, $"Text output below recommended length ({len}/{TextMinimumLength} chars)");
        }

        if (outputType is "mermaid" or "diagram")
        {
            if (content != null && !content.Contains("-->") && !content.Contains("->"))
                return (ReviewSeverity.Major, "Diagram has no edges — may be malformed");
        }

        return (null, null);
    }
}
