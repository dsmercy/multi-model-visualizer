using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MultiModelVisualizer.Api.Data;
using MultiModelVisualizer.Api.Models;

namespace MultiModelVisualizer.Api.Services;

public class GenerationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<Guid> _queue;
    private readonly JobProgressHub _hub;
    private readonly ILogger<GenerationWorker> _logger;

    public GenerationWorker(
        IServiceScopeFactory scopeFactory,
        Channel<Guid> queue,
        JobProgressHub hub,
        ILogger<GenerationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GenerationWorker started.");
        await foreach (var jobId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try { await ProcessJobAsync(jobId, stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Unhandled exception processing job {JobId}", jobId); }
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken ct)
    {
        _logger.LogInformation("Processing generation job {JobId}", jobId);

        // Each job gets its own DI scope → fresh DbContext, services
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pythonAi = scope.ServiceProvider.GetRequiredService<IPythonAiService>();
        var ollama = scope.ServiceProvider.GetRequiredService<IOllamaService>();

        var job = await db.GenerationJobs
            .Include(j => j.Session)
            .FirstOrDefaultAsync(j => j.JobId == jobId, ct);

        if (job?.Session == null)
        {
            _logger.LogWarning("Job {JobId} not found or missing session", jobId);
            return;
        }

        var session = job.Session;

        await UpdateJobAsync(db, job, session, JobStatus.Processing, WorkflowState.Generating, 10, "GenerationStarted", ct);
        _hub.Publish(jobId, new JobProgressEvent(jobId, JobStatus.Processing, 10));

        try
        {
            var components = session.SelectedComponents?.Split(',').Select(c => c.Trim()).ToList() ?? new List<string>();
            var vizType = job.VisualizationType ?? "diagram";

            PythonGenerationResult? pythonResult = null;

            if (vizType is "diagram" or "flowchart")
            {
                try
                {
                    _hub.Publish(jobId, new JobProgressEvent(jobId, JobStatus.Processing, 30));
                    var contract = new GenerationContract(
                        JobId: jobId, SessionId: session.SessionId,
                        VisualizationType: vizType,
                        Concept: session.Topic ?? "unknown",
                        Domain: session.Domain ?? "general",
                        Components: components,
                        Settings: new GenerationSettings(false, true, session.DifficultyLevel ?? "beginner", "educational", "schematic"),
                        FallbackAttempt: job.FallbackAttempt
                    );
                    pythonResult = await pythonAi.GenerateAsync(contract, ct);
                    _hub.Publish(jobId, new JobProgressEvent(jobId, JobStatus.Processing, 70));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Python AI failed for job {JobId}, using text fallback", jobId);
                }
            }

            // Fallback to text
            if (pythonResult == null || pythonResult.Status == "Failed")
            {
                _hub.Publish(jobId, new JobProgressEvent(jobId, JobStatus.Processing, 50));
                var textOutput = await GenerateTextFallbackAsync(ollama, session, components, ct);
                if (textOutput.Length < 200)
                    textOutput += $"\n\nThis explanation covers the key aspects of {session.Topic} including: {string.Join(", ", components)}.";

                if (pythonResult?.Status == "Failed")
                {
                    job.FallbackAttempt++;
                    db.LearningSessionEvents.Add(new LearningSessionEvent
                    {
                        SessionId = session.SessionId,
                        PreviousState = WorkflowState.Generating,
                        NewState = WorkflowState.FallbackGeneration,
                        Trigger = "DiagramFailed",
                        EventPayload = JsonSerializer.Serialize(new { reason = "Python service failed", fallbackAttempt = job.FallbackAttempt })
                    });
                }

                job.OutputType = "text";
                job.OutputContent = textOutput;
                _hub.Publish(jobId, new JobProgressEvent(jobId, JobStatus.Processing, 90));
            }
            else
            {
                job.OutputType = pythonResult.OutputType;
                job.OutputUrl = pythonResult.OutputUrl;
                job.OutputContent = pythonResult.OutputContent;
                _hub.Publish(jobId, new JobProgressEvent(jobId, JobStatus.Processing, 90));
            }

            var validationError = ValidateOutput(job);
            if (validationError != null)
            {
                await FailJobAsync(db, job, session, validationError, ct);
                _hub.Publish(jobId, new JobProgressEvent(jobId, JobStatus.Failed, 0, ErrorCode: validationError));
                _hub.Complete(jobId);
                return;
            }

            await UpdateJobAsync(db, job, session, JobStatus.Completed, WorkflowState.Generated, 100, "GenerationCompleted", ct);

            session.CurrentState = WorkflowState.Completed;
            session.FinalOutput = job.OutputContent;
            session.UpdatedDate = DateTimeOffset.UtcNow;
            db.LearningSessionEvents.Add(new LearningSessionEvent
            {
                SessionId = session.SessionId,
                PreviousState = WorkflowState.Generated,
                NewState = WorkflowState.Completed,
                Trigger = "AutoComplete",
                EventPayload = JsonSerializer.Serialize(new { jobId, outputType = job.OutputType })
            });
            await db.SaveChangesAsync(ct);

            _hub.Publish(jobId, new JobProgressEvent(jobId, JobStatus.Completed, 100, job.OutputType, job.OutputUrl));
            _hub.Complete(jobId);

            _logger.LogInformation("Job {JobId} completed with output type {OutputType}", jobId, job.OutputType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", jobId);
            await FailJobAsync(db, job, session, "INTERNAL_ERROR", ct);
            _hub.Publish(jobId, new JobProgressEvent(jobId, JobStatus.Failed, 0, ErrorCode: "INTERNAL_ERROR"));
            _hub.Complete(jobId);
        }
    }

    private static string? ValidateOutput(GenerationJob job)
    {
        if (job.OutputType == "text" && (job.OutputContent?.Length ?? 0) < 200) return "TEXT_TOO_SHORT";
        if (job.OutputType is "mermaid" or "diagram" or "flowchart" && string.IsNullOrWhiteSpace(job.OutputContent)) return "EMPTY_DIAGRAM";
        if (string.IsNullOrWhiteSpace(job.OutputContent) && string.IsNullOrWhiteSpace(job.OutputUrl)) return "NO_OUTPUT";
        return null;
    }

    private static async Task<string> GenerateTextFallbackAsync(IOllamaService ollama, LearningSession session, List<string> components, CancellationToken ct)
    {
        var prompt = $"Create a detailed educational explanation for \"{session.Topic}\" at {session.DifficultyLevel ?? "beginner"} level.\n" +
                     $"Cover these key components: {string.Join(", ", components)}\n" +
                     $"Context: {session.Explanation}\n\n" +
                     "Provide a structured explanation with clear sections for each component.";
        return await ollama.GenerateAsync(prompt, useJsonFormat: false, ct);
    }

    private static async Task UpdateJobAsync(AppDbContext db, GenerationJob job, LearningSession session,
        string jobStatus, string sessionState, int progress, string trigger, CancellationToken ct)
    {
        job.Status = jobStatus;
        job.Progress = progress;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        db.GenerationJobs.Update(job);

        var prev = session.CurrentState;
        session.CurrentState = sessionState;
        session.UpdatedDate = DateTimeOffset.UtcNow;
        db.LearningSessions.Update(session);

        db.LearningSessionEvents.Add(new LearningSessionEvent
        {
            SessionId = session.SessionId,
            PreviousState = prev,
            NewState = sessionState,
            Trigger = trigger,
            EventPayload = JsonSerializer.Serialize(new { jobId = job.JobId, progress })
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task FailJobAsync(AppDbContext db, GenerationJob job, LearningSession session, string errorCode, CancellationToken ct)
    {
        job.Status = JobStatus.Failed;
        job.ErrorCode = errorCode;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        db.GenerationJobs.Update(job);

        var prev = session.CurrentState;
        session.CurrentState = WorkflowState.Failed;
        session.UpdatedDate = DateTimeOffset.UtcNow;
        db.LearningSessions.Update(session);

        db.LearningSessionEvents.Add(new LearningSessionEvent
        {
            SessionId = session.SessionId,
            PreviousState = prev,
            NewState = WorkflowState.Failed,
            Trigger = "GenerationFailed",
            EventPayload = JsonSerializer.Serialize(new { jobId = job.JobId, errorCode })
        });
        await db.SaveChangesAsync(ct);
    }
}
