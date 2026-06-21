using System.Text;
using System.Text.Json;

namespace MultiModelVisualizer.Api.Services;

public class OllamaService : IOllamaService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _fallbackModel;
    private readonly ILogger<OllamaService> _logger;

    public OllamaService(HttpClient httpClient, IConfiguration config, ILogger<OllamaService> logger)
    {
        _httpClient = httpClient;
        _model = config["AI:OllamaModel"] ?? "gemma3:4b";
        _fallbackModel = config["AI:FallbackOllamaModel"] ?? "qwen2.5-coder:7b";
        _logger = logger;
    }

    public async Task<string> GenerateAsync(string prompt, bool useJsonFormat = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await CallOllamaAsync(_model, prompt, useJsonFormat, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary model {Model} failed, trying fallback {Fallback}", _model, _fallbackModel);
            try
            {
                var result = await CallOllamaAsync(_fallbackModel, prompt, useJsonFormat, cancellationToken);
                return result;
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Both primary and fallback models failed");
                throw new InvalidOperationException("All Ollama models failed to respond.", fallbackEx);
            }
        }
    }

    private async Task<string> CallOllamaAsync(string model, string prompt, bool useJsonFormat, CancellationToken cancellationToken)
    {
        var requestBody = new Dictionary<string, object>
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = false
        };

        if (useJsonFormat)
        {
            requestBody["format"] = "json";
        }

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogDebug("Calling Ollama model {Model} with prompt length {Length}", model, prompt.Length);

        var response = await _httpClient.PostAsync("/api/generate", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseBody);

        if (doc.RootElement.TryGetProperty("response", out var responseElement))
        {
            var text = responseElement.GetString() ?? string.Empty;
            _logger.LogDebug("Ollama response length: {Length}", text.Length);
            return text;
        }

        throw new InvalidOperationException($"Ollama response did not contain 'response' field. Body: {responseBody[..Math.Min(500, responseBody.Length)]}");
    }
}
