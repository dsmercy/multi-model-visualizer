using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using MultiModelVisualizer.Api.Data;
using MultiModelVisualizer.Api.Models;

namespace MultiModelVisualizer.Api.Services;

// Holds per-job progress channels so SSE subscribers can receive updates
public class JobProgressHub
{
    private readonly Dictionary<Guid, List<Channel<JobProgressEvent>>> _channels = new();
    private readonly object _lock = new();

    public Channel<JobProgressEvent> Subscribe(Guid jobId)
    {
        var ch = Channel.CreateUnbounded<JobProgressEvent>();
        lock (_lock)
        {
            if (!_channels.TryGetValue(jobId, out var list))
            {
                list = new List<Channel<JobProgressEvent>>();
                _channels[jobId] = list;
            }
            list.Add(ch);
        }
        return ch;
    }

    public void Unsubscribe(Guid jobId, Channel<JobProgressEvent> ch)
    {
        lock (_lock)
        {
            if (_channels.TryGetValue(jobId, out var list))
                list.Remove(ch);
        }
    }

    public void Publish(Guid jobId, JobProgressEvent evt)
    {
        List<Channel<JobProgressEvent>> snapshot;
        lock (_lock)
        {
            if (!_channels.TryGetValue(jobId, out var list)) return;
            snapshot = list.ToList();
        }
        foreach (var ch in snapshot)
            ch.Writer.TryWrite(evt);
    }

    public void Complete(Guid jobId)
    {
        List<Channel<JobProgressEvent>> snapshot;
        lock (_lock)
        {
            if (!_channels.TryGetValue(jobId, out var list)) return;
            snapshot = list.ToList();
            _channels.Remove(jobId);
        }
        foreach (var ch in snapshot)
            ch.Writer.TryComplete();
    }
}

public class GenerationJobService : IGenerationJobService
{
    private readonly AppDbContext _db;
    private readonly Channel<Guid> _queue;
    private readonly JobProgressHub _hub;

    public GenerationJobService(AppDbContext db, Channel<Guid> queue, JobProgressHub hub)
    {
        _db = db;
        _queue = queue;
        _hub = hub;
    }

    public async Task<GenerationJob> EnqueueAsync(LearningSession session, CancellationToken ct = default)
    {
        var db = _db;

        var job = new GenerationJob
        {
            SessionId = session.SessionId,
            VisualizationType = session.VisualizationType ?? "diagram",
            Status = JobStatus.Queued,
            Progress = 0,
        };
        db.GenerationJobs.Add(job);

        var evt = new LearningSessionEvent
        {
            SessionId = session.SessionId,
            PreviousState = session.CurrentState,
            NewState = WorkflowState.GenerationQueued,
            Trigger = "Approve",
            EventPayload = JsonSerializer.Serialize(new { jobId = job.JobId })
        };
        db.LearningSessionEvents.Add(evt);

        session.CurrentState = WorkflowState.GenerationQueued;
        session.UpdatedDate = DateTimeOffset.UtcNow;
        db.LearningSessions.Update(session);

        await db.SaveChangesAsync(ct);

        await _queue.Writer.WriteAsync(job.JobId, ct);
        return job;
    }

    public async Task<GenerationJob?> GetJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _db.GenerationJobs.FindAsync(new object[] { jobId }, ct);
    }

    public async IAsyncEnumerable<JobProgressEvent> StreamProgressAsync(Guid jobId, [EnumeratorCancellation] CancellationToken ct)
    {
        // Emit current state immediately so reconnecting clients get the latest
        {
            var job = await _db.GenerationJobs.FindAsync(new object[] { jobId }, ct);
            if (job != null)
            {
                yield return new JobProgressEvent(job.JobId, job.Status, job.Progress, job.OutputType, job.OutputUrl);
                if (job.Status is JobStatus.Completed or JobStatus.Failed)
                    yield break;
            }
        }

        var ch = _hub.Subscribe(jobId);
        try
        {
            await foreach (var evt in ch.Reader.ReadAllAsync(ct))
            {
                yield return evt;
                if (evt.Status is JobStatus.Completed or JobStatus.Failed)
                    break;
            }
        }
        finally
        {
            _hub.Unsubscribe(jobId, ch);
        }
    }
}
