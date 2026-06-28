using MultiModelVisualizer.Api.Models;

namespace MultiModelVisualizer.Api.Services;

public interface IWorkflowEngine
{
    Task<SendMessageResponse> ProcessMessageAsync(LearningSession session, string userMessage, CancellationToken cancellationToken = default);
    Task<ApproveResponse> ApproveAsync(LearningSession session, CancellationToken cancellationToken = default);
    Task<SendMessageResponse> RefineAsync(LearningSession session, CancellationToken cancellationToken = default);
    // Phase 4
    Task<ResumeResponse> ResumeAsync(LearningSession session, CancellationToken cancellationToken = default);
    Task<CloneResponse> CloneAsync(LearningSession session, CancellationToken cancellationToken = default);
    Task<CancelResponse> CancelAsync(LearningSession session, CancellationToken cancellationToken = default);
}
