using MultiModelVisualizer.Api.Models;

namespace MultiModelVisualizer.Api.Services;

public interface IPythonAiService
{
    Task<PythonGenerationResult> GenerateAsync(GenerationContract contract, CancellationToken ct = default);
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}
