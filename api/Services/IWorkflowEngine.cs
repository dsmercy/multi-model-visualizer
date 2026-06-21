using MultiModelVisualizer.Api.Models;

namespace MultiModelVisualizer.Api.Services;

public interface IWorkflowEngine
{
    Task<SendMessageResponse> ProcessMessageAsync(LearningSession session, string userMessage, CancellationToken cancellationToken = default);
    Task<ApproveResponse> ApproveAsync(LearningSession session, CancellationToken cancellationToken = default);
    Task<SendMessageResponse> RefineAsync(LearningSession session, CancellationToken cancellationToken = default);
}
