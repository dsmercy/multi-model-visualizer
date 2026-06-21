namespace MultiModelVisualizer.Api.Services;

public interface IOllamaService
{
    Task<string> GenerateAsync(string prompt, bool useJsonFormat = false, CancellationToken cancellationToken = default);
}
