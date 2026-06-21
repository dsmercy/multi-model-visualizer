using MultiModelVisualizer.Api.Models;

namespace MultiModelVisualizer.Api.Services;

public interface IWorkflowEngine
{
    Task<SendMessageResponse> ProcessMessageAsync(LearningSession session, string userMessage, CancellationToken cancellationToken = default);
}
