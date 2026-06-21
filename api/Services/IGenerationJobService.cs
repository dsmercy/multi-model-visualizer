using MultiModelVisualizer.Api.Models;

namespace MultiModelVisualizer.Api.Services;

public interface IGenerationJobService
{
    Task<GenerationJob> EnqueueAsync(LearningSession session, CancellationToken ct = default);
    Task<GenerationJob?> GetJobAsync(Guid jobId, CancellationToken ct = default);
    IAsyncEnumerable<JobProgressEvent> StreamProgressAsync(Guid jobId, CancellationToken ct);
}
