namespace MultiModelVisualizer.Api.Services;

public interface IReviewService
{
    Task ReviewAsync(Guid jobId, string outputType, string? outputContent, CancellationToken ct = default);
}
