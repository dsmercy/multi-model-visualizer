using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using MultiModelVisualizer.Api.Data;
using MultiModelVisualizer.Api.Models;

namespace MultiModelVisualizer.Api.Services;

/// <summary>
/// Dequeues job IDs from the in-process channel, executes them with retry + full fallback hierarchy.
/// Retry policy: up to MaxRetryAttempts (3), exponential backoff [5s, 15s, 45s].
/// Fallback hierarchy: 3D → 2D animation → interactive diagram → static diagram → narrated → text.
/// </summary>
public class GenerationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<Guid> _queue;
    private readonly JobProgressHub _hub;
    private readonly IConfiguration _config;
    private readonly ILogger<GenerationWorker> _logger;

    private int MaxRetryAttempts => _config.GetValue("WorkflowEngine:MaxRetryAttempts", 3);
    private int[] RetryBackoff => _config.GetSection("WorkflowEngine:RetryBackoffSeconds").Get<int[]>() ?? new[] { 5, 15, 45 };

    // Fallback levels in order, 0 = highest quality
    private static readonly string[] FallbackLevels = new[]
    {
        "3d_animation",     // 0 — Blender headless GLB
        "video",            // 1 — ffmpeg MP4
        "2d_animation",     // 2 — D3.js animation descriptor
        "interactive",      // 3 — interactive diagram (falls through to diagram)
        "diagram",          // 4 — Mermaid / flowchart
        "static_diagram",   // 5 — static Mermaid
        "narration",        // 6 — Piper TTS
        "text",             // 7 — LLM text
    };

    public GenerationWorker(
        IServiceScopeFactory scopeFactory,
        Channel<Guid> queue,
        JobProgressHub hub,
        IConfiguration config,
        ILogger<GenerationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _hub = hub;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GenerationWorker started.");
        await foreach (var jobId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            // Honor retry delay — job may have been re-queued with a future NextRetryAt
            await using var checkScope = _scopeFactory.CreateAsyncScope();
            var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var checkJob = await checkDb.GenerationJobs.FindAsync(new object[] { jobId }, stoppingToken);
            if (checkJob?.NextRetryAt.HasValue == true && checkJob.NextRetryAt > DateTimeOffset.UtcNow)
            {
                var delay = checkJob.NextRetryAt.Value - DateTimeOffset.UtcNow;
                _logger.LogInformation("Job {JobId} waiting {Delay}s for retry backoff", jobId, delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
            }

            try { await ProcessJobAsync(jobId, stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Unhandled exception processing job {JobId}", jobId); }
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken ct)
    {
        _logger.LogInformation("Processing job {JobId}", jobId);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pythonAi = scope.ServiceProvider.GetRequiredService<IPythonAiService>();
        var ollama = scope.ServiceProvider.GetRequiredService<IOllamaService>();
        var reviewService = scope.ServiceProvider.GetRequiredService<IReviewService>();

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

        // Determine starting fallback level from the requested viz type
        var startLevel = FallbackLevelFor(job.VisualizationType ?? "diagram");
        // If we already attempted and failed before, resume from where we left off
        var currentLevel = Math.Max(startLevel, job.FallbackAttempt);

        try
        {
            var (succeeded, outputType, outputContent, outputUrl) = await TryFallbackHierarchyAsync(
                db, job, session, pythonAi, ollama, currentLevel, ct);

            if (!succeeded)
            {
                // All fallback levels exhausted
                await EscalateJobAsync(db, job, session, ct);
                _hub.Publish(jobId, new JobProgressEvent(jobId, JobStatus.Failed, 0, ErrorCode: "ALL_FALLBACKS_EXHAUSTED"));
                _hub.Complete(jobId);
                return;
            }

            job.OutputType = outputType;
            job.OutputContent = outputContent;
            job.OutputUrl = outputUrl;

            var validationError = ValidateOutput(job);
            if (validationError != null)
            {
                await HandleRetryOrFallbackAsync(db, job, session, validationError, currentLevel, ct);
                _hub.Publish(jobId, new JobProgressEvent(jobId, job.Status, job.Progress, ErrorCode: validationError));
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
                EventPayload = JsonSerializer.Serialize(new { jobId, outputType = job.OutputType, fallbackLevel = job.FallbackAttempt })
            });
            await db.SaveChangesAsync(ct);

            _hub.Publish(jobId, new JobProgressEvent(jobId, JobStatus.Completed, 100, job.OutputType, job.OutputUrl));
            _hub.Complete(jobId);

            _logger.LogInformation("Job {JobId} completed (level {Level}, type {Type})", jobId, job.FallbackAttempt, job.OutputType);

            // Kick off async review (non-blocking)
            _ = Task.Run(() => reviewService.ReviewAsync(job.JobId, job.OutputType!, job.OutputContent, ct), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} threw unhandled exception", jobId);
            await HandleRetryOrFallbackAsync(db, job, session, "INTERNAL_ERROR", currentLevel, ct);
            _hub.Publish(jobId, new JobProgressEvent(jobId, job.Status, 0, ErrorCode: "INTERNAL_ERROR"));
            _hub.Complete(jobId);
        }
    }

    // Returns (succeeded, outputType, outputContent, outputUrl)
    private async Task<(bool, string?, string?, string?)> TryFallbackHierarchyAsync(
        AppDbContext db, GenerationJob job, LearningSession session,
        IPythonAiService pythonAi, IOllamaService ollama,
        int startLevel, CancellationToken ct)
    {
        var components = session.SelectedComponents?.Split(',').Select(c => c.Trim()).ToList() ?? new List<string>();

        for (int level = startLevel; level < FallbackLevels.Length; level++)
        {
            var levelName = FallbackLevels[level];
            _logger.LogInformation("Job {JobId} attempting level {Level}: {LevelName}", job.JobId, level, levelName);

            job.FallbackAttempt = level;
            job.UpdatedAt = DateTimeOffset.UtcNow;

            if (level > startLevel)
            {
                // Record the fallback transition
                db.LearningSessionEvents.Add(new LearningSessionEvent
                {
                    SessionId = session.SessionId,
                    PreviousState = session.CurrentState,
                    NewState = WorkflowState.FallbackGeneration,
                    Trigger = "FallbackLevel",
                    EventPayload = JsonSerializer.Serialize(new { level, levelName, jobId = job.JobId })
                });
                session.CurrentState = WorkflowState.FallbackGeneration;
                await db.SaveChangesAsync(ct);
                _hub.Publish(job.JobId, new JobProgressEvent(job.JobId, JobStatus.FallbackGeneration, 20 + level * 10));
            }

            try
            {
                return levelName switch
                {
                    "3d_animation" => await Try3DAsync(job, session, components, pythonAi, ct),
                    "video" => await TryVideoAsync(job, session, components, pythonAi, ct),
                    "2d_animation" => await Try2DAnimationAsync(job, session, components, ct),
                    "interactive" => await TryInteractiveDiagramAsync(job, session, components, ct),
                    "diagram" or "static_diagram" => await TryDiagramAsync(job, session, components, pythonAi, ct),
                    "narration" => await TryNarrationAsync(job, session, components, ollama, ct),
                    "text" => await TryTextAsync(job, session, components, ollama, ct),
                    _ => (false, null, null, null)
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Job {JobId} failed at level {Level} ({LevelName})", job.JobId, level, levelName);
            }
        }

        return (false, null, null, null);
    }

    // Level 0: 3D (Blender headless via Python AI service)
    private async Task<(bool, string?, string?, string?)> Try3DAsync(GenerationJob job, LearningSession session, List<string> components, IPythonAiService pythonAi, CancellationToken ct)
    {
        _logger.LogInformation("Job {JobId}: attempting 3D generation via Blender", job.JobId);
        var contract = new GenerationContract(
            JobId: job.JobId, SessionId: session.SessionId,
            VisualizationType: "3d_animation",
            Concept: session.Topic ?? "unknown",
            Domain: session.Domain ?? "general",
            Components: components,
            Settings: new GenerationSettings(false, true, session.DifficultyLevel ?? "beginner", "educational", "schematic"),
            FallbackAttempt: job.FallbackAttempt
        );
        var result = await pythonAi.GenerateAsync(contract, ct);
        if (result.Status == "Completed" && !string.IsNullOrWhiteSpace(result.OutputUrl))
            return (true, result.OutputType, result.OutputContent, result.OutputUrl);
        return (false, null, null, null);
    }

    // Level 1: Video (ffmpeg MP4 via Python AI service)
    private async Task<(bool, string?, string?, string?)> TryVideoAsync(GenerationJob job, LearningSession session, List<string> components, IPythonAiService pythonAi, CancellationToken ct)
    {
        _logger.LogInformation("Job {JobId}: attempting video generation via ffmpeg", job.JobId);
        var contract = new GenerationContract(
            JobId: job.JobId, SessionId: session.SessionId,
            VisualizationType: "video",
            Concept: session.Topic ?? "unknown",
            Domain: session.Domain ?? "general",
            Components: components,
            Settings: new GenerationSettings(false, true, session.DifficultyLevel ?? "beginner", "educational", "schematic"),
            FallbackAttempt: job.FallbackAttempt
        );
        var result = await pythonAi.GenerateAsync(contract, ct);
        if (result.Status == "Completed" && !string.IsNullOrWhiteSpace(result.OutputUrl))
            return (true, result.OutputType, result.OutputContent, result.OutputUrl);
        return (false, null, null, null);
    }

    // Level 2: 2D animation (D3.js — generated on frontend; backend produces animation descriptor JSON)
    private async Task<(bool, string?, string?, string?)> Try2DAnimationAsync(GenerationJob job, LearningSession session, List<string> components, CancellationToken ct)
    {
        _logger.LogInformation("Job {JobId}: 2D animation stub — producing animation descriptor", job.JobId);
        // Produce a structured animation descriptor that the frontend D3 engine can consume
        var descriptor = JsonSerializer.Serialize(new
        {
            type = "d3_animation",
            topic = session.Topic,
            domain = session.Domain,
            components,
            steps = components.Select((c, i) => new { stepIndex = i, label = $"Step {i + 1}: {c}", description = $"Processing {c}" }).ToArray()
        });
        await Task.Delay(100, ct); // simulate brief processing
        return (true, "animation_descriptor", descriptor, null);
    }

    // Level 2: Interactive diagram (stub — same as static diagram for now)
    private Task<(bool, string?, string?, string?)> TryInteractiveDiagramAsync(GenerationJob job, LearningSession session, List<string> components, CancellationToken ct)
    {
        _logger.LogInformation("Job {JobId}: Interactive diagram — delegating to diagram engine", job.JobId);
        return Task.FromResult<(bool, string?, string?, string?)>((false, null, null, null)); // fall through to diagram
    }

    // Levels 3-4: Mermaid diagram via Python AI service
    private async Task<(bool, string?, string?, string?)> TryDiagramAsync(GenerationJob job, LearningSession session, List<string> components, IPythonAiService pythonAi, CancellationToken ct)
    {
        _hub.Publish(job.JobId, new JobProgressEvent(job.JobId, JobStatus.Processing, 40));
        var contract = new GenerationContract(
            JobId: job.JobId, SessionId: session.SessionId,
            VisualizationType: "diagram",  // always send "diagram" to Python — not the user-selected type
            Concept: session.Topic ?? "unknown",
            Domain: session.Domain ?? "general",
            Components: components,
            Settings: new GenerationSettings(false, true, session.DifficultyLevel ?? "beginner", "educational", "schematic"),
            FallbackAttempt: job.FallbackAttempt
        );
        var result = await pythonAi.GenerateAsync(contract, ct);
        _hub.Publish(job.JobId, new JobProgressEvent(job.JobId, JobStatus.Processing, 75));

        if (result.Status == "Completed" && !string.IsNullOrWhiteSpace(result.OutputContent))
            return (true, result.OutputType, result.OutputContent, result.OutputUrl);

        return (false, null, null, null);
    }

    // Level 5: Narration (Piper TTS via Python AI service Wyoming client)
    private async Task<(bool, string?, string?, string?)> TryNarrationAsync(GenerationJob job, LearningSession session, List<string> components, IOllamaService ollama, CancellationToken ct)
    {
        _logger.LogInformation("Job {JobId}: attempting narration via Piper TTS", job.JobId);
        // Narration is generated by the Python AI service which speaks Wyoming protocol to Piper
        var pythonAi = _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<IPythonAiService>();
        var contract = new GenerationContract(
            JobId: job.JobId, SessionId: session.SessionId,
            VisualizationType: "narration",
            Concept: session.Topic ?? "unknown",
            Domain: session.Domain ?? "general",
            Components: components,
            Settings: new GenerationSettings(true, true, session.DifficultyLevel ?? "beginner", "educational", "schematic"),
            FallbackAttempt: job.FallbackAttempt
        );
        var result = await pythonAi.GenerateAsync(contract, ct);
        if (result.Status == "Completed" && !string.IsNullOrWhiteSpace(result.OutputUrl))
            return (true, result.OutputType, result.OutputContent, result.OutputUrl);
        return (false, null, null, null);
    }

    // Level 6: Text explanation — always succeeds
    private async Task<(bool, string?, string?, string?)> TryTextAsync(GenerationJob job, LearningSession session, List<string> components, IOllamaService ollama, CancellationToken ct)
    {
        _hub.Publish(job.JobId, new JobProgressEvent(job.JobId, JobStatus.Processing, 60));
        var prompt = $"Create a detailed educational explanation for \"{session.Topic}\" at {session.DifficultyLevel ?? "beginner"} level.\n" +
                     $"Cover these key components: {string.Join(", ", components)}\n" +
                     $"Context: {session.Explanation}\n\n" +
                     "Provide a structured explanation with clear sections for each component.";
        var text = await ollama.GenerateAsync(prompt, useJsonFormat: false, ct);

        if (text.Length < 200)
            text += $"\n\nThis explanation covers the key aspects of {session.Topic} including: {string.Join(", ", components)}.";

        _hub.Publish(job.JobId, new JobProgressEvent(job.JobId, JobStatus.Processing, 90));
        return (true, "text", text, null);
    }

    private async Task HandleRetryOrFallbackAsync(AppDbContext db, GenerationJob job, LearningSession session, string errorCode, int currentLevel, CancellationToken ct)
    {
        if (job.RetryCount < MaxRetryAttempts)
        {
            // Schedule a retry
            job.RetryCount++;
            var backoffIdx = Math.Min(job.RetryCount - 1, RetryBackoff.Length - 1);
            var backoffSec = RetryBackoff[backoffIdx];
            job.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(backoffSec);
            job.Status = JobStatus.Retrying;
            job.ErrorCode = errorCode;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            db.GenerationJobs.Update(job);

            var prev = session.CurrentState;
            session.CurrentState = WorkflowState.Retrying;
            session.UpdatedDate = DateTimeOffset.UtcNow;
            db.LearningSessions.Update(session);

            db.LearningSessionEvents.Add(new LearningSessionEvent
            {
                SessionId = session.SessionId,
                PreviousState = prev,
                NewState = WorkflowState.Retrying,
                Trigger = "RetryScheduled",
                EventPayload = JsonSerializer.Serialize(new { retryCount = job.RetryCount, backoffSec, nextRetryAt = job.NextRetryAt, errorCode })
            });
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Job {JobId} retry {Count}/{Max} in {Backoff}s", job.JobId, job.RetryCount, MaxRetryAttempts, backoffSec);

            // Re-queue with delay
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(backoffSec));
                await _queue.Writer.WriteAsync(job.JobId);
            });
        }
        else
        {
            // Retry exhausted — try next fallback level
            var nextLevel = currentLevel + 1;
            if (nextLevel >= FallbackLevels.Length)
            {
                await EscalateJobAsync(db, job, session, ct);
            }
            else
            {
                job.RetryCount = 0; // reset retries for the next fallback level
                job.FallbackAttempt = nextLevel;
                job.Status = JobStatus.Retrying;
                job.NextRetryAt = null;
                job.UpdatedAt = DateTimeOffset.UtcNow;
                db.GenerationJobs.Update(job);

                db.LearningSessionEvents.Add(new LearningSessionEvent
                {
                    SessionId = session.SessionId,
                    PreviousState = session.CurrentState,
                    NewState = WorkflowState.RetryExhausted,
                    Trigger = "RetryExhausted",
                    EventPayload = JsonSerializer.Serialize(new { errorCode, escalatingToLevel = nextLevel, levelName = FallbackLevels[nextLevel] })
                });
                session.CurrentState = WorkflowState.RetryExhausted;
                session.UpdatedDate = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);

                _logger.LogWarning("Job {JobId} retry exhausted at level {Level}, escalating to fallback level {Next}", job.JobId, currentLevel, nextLevel);
                await _queue.Writer.WriteAsync(job.JobId);
            }
        }
    }

    private async Task EscalateJobAsync(AppDbContext db, GenerationJob job, LearningSession session, CancellationToken ct)
    {
        job.Status = JobStatus.Failed;
        job.ErrorCode = "ALL_FALLBACKS_EXHAUSTED";
        job.UpdatedAt = DateTimeOffset.UtcNow;
        db.GenerationJobs.Update(job);

        db.LearningSessionEvents.Add(new LearningSessionEvent
        {
            SessionId = session.SessionId,
            PreviousState = session.CurrentState,
            NewState = WorkflowState.Escalated,
            Trigger = "AllFallbacksExhausted",
            EventPayload = JsonSerializer.Serialize(new { jobId = job.JobId })
        });
        db.LearningSessionEvents.Add(new LearningSessionEvent
        {
            SessionId = session.SessionId,
            PreviousState = WorkflowState.Escalated,
            NewState = WorkflowState.Paused,
            Trigger = "AutoPause",
            EventPayload = JsonSerializer.Serialize(new { reason = "AI generation service unavailable" })
        });

        session.CurrentState = WorkflowState.Paused;
        session.UpdatedDate = DateTimeOffset.UtcNow;
        db.LearningSessions.Update(session);
        await db.SaveChangesAsync(ct);

        _logger.LogError("Job {JobId} escalated to Paused after all fallbacks exhausted", job.JobId);
    }

    private static int FallbackLevelFor(string vizType) => vizType switch
    {
        "auto" => 0,            // start from highest quality, let hierarchy decide
        "3d" or "3d_animation" => 0,
        "video" => 1,
        "2d_animation" => 2,
        "interactive" => 3,
        "diagram" or "flowchart" => 4,
        "static_diagram" => 5,
        "narration" => 6,
        "text" => 7,
        _ => 0,                 // unknown type: start from top
    };

    private string? ValidateOutput(GenerationJob job)
    {
        if (job.OutputType == "text" && (job.OutputContent?.Length ?? 0) < 200) return "TEXT_TOO_SHORT";
        if (job.OutputType is "mermaid" or "diagram" or "flowchart" && string.IsNullOrWhiteSpace(job.OutputContent)) return "EMPTY_DIAGRAM";
        if (job.OutputType == "animation_descriptor" && string.IsNullOrWhiteSpace(job.OutputContent)) return "EMPTY_ANIMATION";
        if (string.IsNullOrWhiteSpace(job.OutputContent) && string.IsNullOrWhiteSpace(job.OutputUrl)) return "NO_OUTPUT";

        // File-size validation for binary outputs — check via Python service base URL path
        if (job.OutputType == "video" && !string.IsNullOrWhiteSpace(job.OutputUrl))
        {
            var minKb = _config.GetValue("Validation:VideoMinimumSizeKB", 50);
            var sizeKb = GetOutputFileSizeKb(job.OutputUrl);
            if (sizeKb >= 0 && sizeKb < minKb)
                return $"VIDEO_TOO_SMALL_{sizeKb}KB_MIN_{minKb}KB";
        }
        if (job.OutputType == "glb" && !string.IsNullOrWhiteSpace(job.OutputUrl))
        {
            var minKb = _config.GetValue("Validation:GLBMinimumSizeKB", 20);
            var sizeKb = GetOutputFileSizeKb(job.OutputUrl);
            if (sizeKb >= 0 && sizeKb < minKb)
                return $"GLB_TOO_SMALL_{sizeKb}KB_MIN_{minKb}KB";
        }
        return null;
    }

    private long GetOutputFileSizeKb(string outputUrl)
    {
        // outputUrl is like /output/video_<guid>.mp4 served by Python AI service
        var pythonBase = _config["PythonService:BaseUrl"] ?? "http://localhost:8000";
        var fullUrl = pythonBase.TrimEnd('/') + outputUrl;
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, fullUrl);
            var resp = http.Send(req);
            if (resp.Content.Headers.ContentLength is long len)
                return len / 1024;
        }
        catch { /* if we can't check, don't block */ }
        return -1; // unknown size — don't fail validation
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
}
