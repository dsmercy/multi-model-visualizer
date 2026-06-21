using System.Text;
using System.Text.Json;
using MultiModelVisualizer.Api.Models;

namespace MultiModelVisualizer.Api.Services;

public class PythonAiService : IPythonAiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonAiService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public PythonAiService(HttpClient httpClient, ILogger<PythonAiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PythonGenerationResult> GenerateAsync(GenerationContract contract, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(contract, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogDebug("Calling Python AI service for job {JobId}", contract.JobId);

        var response = await _httpClient.PostAsync("/generate", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Python AI service returned {StatusCode} for job {JobId}: {Body}",
                response.StatusCode, contract.JobId, body);
            return new PythonGenerationResult(
                contract.JobId, "Failed", null, null, null,
                "PYTHON_SERVICE_ERROR", true, null);
        }

        var result = JsonSerializer.Deserialize<PythonGenerationResult>(body, JsonOpts);
        return result ?? new PythonGenerationResult(
            contract.JobId, "Failed", null, null, null,
            "INVALID_RESPONSE", true, null);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
