using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MultiModelVisualizer.Api.Data;
using MultiModelVisualizer.Api.Models;

namespace MultiModelVisualizer.Api.Services;

public class WorkflowEngine : IWorkflowEngine
{
    private readonly AppDbContext _db;
    private readonly IOllamaService _ollama;
    private readonly IConfiguration _config;
    private readonly ILogger<WorkflowEngine> _logger;

    private double IntentThreshold => _config.GetValue<double>("WorkflowEngine:IntentConfidenceThreshold", 0.75);
    private double DomainThreshold => _config.GetValue<double>("WorkflowEngine:DomainConfidenceThreshold", 0.70);

    public WorkflowEngine(AppDbContext db, IOllamaService ollama, IConfiguration config, ILogger<WorkflowEngine> logger)
    {
        _db = db;
        _ollama = ollama;
        _config = config;
        _logger = logger;
    }

    public async Task<SendMessageResponse> ProcessMessageAsync(LearningSession session, string userMessage, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing message for session {SessionId} in state {State}", session.SessionId, session.CurrentState);

        var (responseMessage, newState) = session.CurrentState switch
        {
            WorkflowState.Created => await HandleCreatedAsync(session, userMessage, cancellationToken),
            WorkflowState.IntentAnalyzed => await HandleIntentAnalyzedAsync(session, userMessage, cancellationToken),
            WorkflowState.DomainClassified => await HandleDomainClassifiedAsync(session, userMessage, cancellationToken),
            WorkflowState.ConceptExplained => await HandleConceptExplainedAsync(session, userMessage, cancellationToken),
            WorkflowState.ComponentSelectionPending => await HandleComponentSelectionAsync(session, userMessage, cancellationToken),
            WorkflowState.VisualizationPlanned => await HandleVisualizationPlannedAsync(session, userMessage, cancellationToken),
            WorkflowState.ApprovalPending => await HandleApprovalPendingAsync(session, userMessage, cancellationToken),
            WorkflowState.Completed => (
                "This session is already completed. Please start a new session to explore another topic.",
                WorkflowState.Completed
            ),
            _ => throw new InvalidOperationException($"Unknown workflow state: {session.CurrentState}")
        };

        var previousState = session.CurrentState;

        if (newState != previousState)
        {
            session.CurrentState = newState;
        }

        session.UpdatedDate = DateTimeOffset.UtcNow;

        var payloadObj = new
        {
            userMessage = userMessage.Length > 500 ? userMessage[..500] : userMessage,
            responseMessage = responseMessage.Length > 500 ? responseMessage[..500] : responseMessage
        };

        var evt = new LearningSessionEvent
        {
            SessionId = session.SessionId,
            PreviousState = previousState,
            NewState = newState,
            Trigger = "UserMessage",
            EventPayload = JsonSerializer.Serialize(payloadObj)
        };

        _db.LearningSessionEvents.Add(evt);
        await _db.SaveChangesAsync(cancellationToken);

        return new SendMessageResponse(responseMessage, newState, session.SessionId);
    }

    // Created -> IntentAnalyzed
    private async Task<(string message, string newState)> HandleCreatedAsync(LearningSession session, string userMessage, CancellationToken ct)
    {
        var prompt = "Extract the learning intent from this message. The user wants to learn something.\n" +
                     $"Message: \"{userMessage}\"\n\n" +
                     "Return JSON with this exact structure:\n" +
                     "{\"intent\": \"brief description of what they want to learn\", \"topic\": \"the specific topic name\", \"confidence\": 0.85}\n\n" +
                     "Confidence should be between 0 and 1. Set confidence below 0.75 if the message is vague or unclear.";

        var llmResponse = await _ollama.GenerateAsync(prompt, useJsonFormat: true, ct);
        var result = ParseJson<IntentAnalysisResult>(llmResponse);

        if (result == null || result.Confidence < IntentThreshold)
        {
            session.CurrentState = WorkflowState.Created;
            return (
                "I'd love to help you learn! Could you be more specific about what you'd like to understand? For example: 'Explain how TCP/IP networking works' or 'Help me understand photosynthesis'.",
                WorkflowState.Created
            );
        }

        session.Intent = result.Intent;
        session.Topic = result.Topic;
        session.CurrentState = WorkflowState.IntentAnalyzed;

        return await HandleIntentAnalyzedAsync(session, userMessage, ct);
    }

    // IntentAnalyzed -> DomainClassified
    private async Task<(string message, string newState)> HandleIntentAnalyzedAsync(LearningSession session, string userMessage, CancellationToken ct)
    {
        var topicToClassify = session.Topic ?? userMessage;
        var prompt = $"Classify the academic domain of this topic: \"{topicToClassify}\"\n" +
                     $"Intent: \"{session.Intent}\"\n\n" +
                     "Return JSON with this exact structure:\n" +
                     "{\"domain\": \"computer_science\", \"subdomain\": \"networking\", \"confidence\": 0.90}\n\n" +
                     "Domain must be one of: computer_science, mechanical_engineering, physics, biology, chemistry, mathematics, other\n" +
                     "Confidence should be between 0 and 1.";

        var llmResponse = await _ollama.GenerateAsync(prompt, useJsonFormat: true, ct);
        var result = ParseJson<DomainClassificationResult>(llmResponse);

        if (result == null)
        {
            result = new DomainClassificationResult("other", "general", 0.5);
        }

        session.Domain = result.Domain;
        session.CurrentState = WorkflowState.DomainClassified;

        return await HandleDomainClassifiedAsync(session, userMessage, ct);
    }

    // DomainClassified -> ConceptExplained
    private async Task<(string message, string newState)> HandleDomainClassifiedAsync(LearningSession session, string userMessage, CancellationToken ct)
    {
        // Step 1: get plain-text explanation
        var explanationPrompt = $"You are an expert educator. Explain \"{session.Topic}\" ({session.Domain}) in 2-3 clear paragraphs suitable for a general audience. Focus on how it works and why it matters.";
        var explanation = await _ollama.GenerateAsync(explanationPrompt, useJsonFormat: false, ct);
        if (string.IsNullOrWhiteSpace(explanation))
            explanation = $"Let me explain {session.Topic}. This is an important concept in {session.Domain}.";

        // Step 2: extract components as simple JSON
        var componentsPrompt = $"List 5 key components or parts of \"{session.Topic}\" that could be visualized in a diagram. Return only a JSON array of strings, for example: [\"Component A\", \"Component B\", \"Component C\", \"Component D\", \"Component E\"]";
        var componentsResponse = await _ollama.GenerateAsync(componentsPrompt, useJsonFormat: true, ct);

        List<string> components;
        try
        {
            var cleaned = Regex.Replace(componentsResponse, @"```(?:json)?\s*", "").Replace("```", "").Trim();
            using var doc = JsonDocument.Parse(cleaned);
            components = new List<string>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                // Bare array: ["a", "b", ...]
                foreach (var el in doc.RootElement.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String)
                        components.Add(el.GetString()!);
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        // {"components": ["a", "b", ...]}
                        foreach (var el in prop.Value.EnumerateArray())
                            if (el.ValueKind == JsonValueKind.String)
                                components.Add(el.GetString()!);
                        break;
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        // {"component1": "a", "component2": "b", ...}
                        components.Add(prop.Value.GetString()!);
                    }
                }
            }
        }
        catch
        {
            components = new List<string>();
        }

        if (components.Count == 0)
            components = new List<string> { "Overview", "Key Concepts", "Core Mechanism", "Applications", "Examples" };

        session.Explanation = explanation;
        session.SelectedComponents = string.Join(", ", components);
        session.CurrentState = WorkflowState.ConceptExplained;

        return await HandleConceptExplainedAsync(session, userMessage, ct);
    }

    // ConceptExplained -> ComponentSelectionPending
    private async Task<(string message, string newState)> HandleConceptExplainedAsync(LearningSession session, string userMessage, CancellationToken ct)
    {
        var components = session.SelectedComponents ?? "Overview, Key Concepts, Applications, Examples";
        var availableComponents = components.Split(',').Select(c => c.Trim()).ToList();
        var numberedList = string.Join("\n", availableComponents.Select((c, i) => $"{i + 1}. {c}"));

        var message = $"**Topic: {session.Topic}**\n\n" +
                      $"{session.Explanation}\n\n" +
                      "---\n\n" +
                      "**Available Components for Visualization:**\n" +
                      $"{numberedList}\n\n" +
                      "To create your visualization, please tell me:\n" +
                      "1. **Which components** would you like to include? (e.g., \"1, 2, 3\" or \"all\" or list them by name)\n" +
                      "2. **Difficulty level**: beginner, intermediate, or expert\n" +
                      "3. **Visualization type**: diagram, flowchart, or text\n\n" +
                      "Example: \"Include components 1, 2, 3 - beginner level - diagram\"";

        session.CurrentState = WorkflowState.ComponentSelectionPending;
        return (message, WorkflowState.ComponentSelectionPending);
    }

    // ComponentSelectionPending -> VisualizationPlanned
    private async Task<(string message, string newState)> HandleComponentSelectionAsync(LearningSession session, string userMessage, CancellationToken ct)
    {
        var availableComponents = session.SelectedComponents ?? "Overview, Key Concepts, Applications, Examples";

        var prompt = $"The user is selecting components for a visualization about \"{session.Topic}\".\n\n" +
                     $"Available components: {availableComponents}\n" +
                     $"User's selection message: \"{userMessage}\"\n\n" +
                     "Parse the user's message and return JSON:\n" +
                     "{\n" +
                     "  \"visualizationType\": \"diagram\",\n" +
                     "  \"components\": [\"component1\", \"component2\"],\n" +
                     "  \"difficulty\": \"beginner\",\n" +
                     "  \"description\": \"A brief description of what the visualization will show\"\n" +
                     "}\n\n" +
                     "Rules:\n" +
                     "- visualizationType must be: diagram, flowchart, or text. Default to \"diagram\" if unclear.\n" +
                     "- difficulty must be: beginner, intermediate, or expert. Default to \"beginner\" if unclear.\n" +
                     "- components must be a non-empty array. If user says \"all\", include all available components.\n" +
                     "- description should describe what the visualization will depict.";

        var llmResponse = await _ollama.GenerateAsync(prompt, useJsonFormat: true, ct);
        var result = ParseJson<VisualizationPlanResult>(llmResponse);

        if (result == null || result.Components == null || result.Components.Count == 0)
        {
            return (
                "I couldn't parse your component selection. Please try again — for example: \"Include Overview and Key Concepts, beginner level, diagram format\"",
                WorkflowState.ComponentSelectionPending
            );
        }

        session.SelectedComponents = string.Join(", ", result.Components);
        session.DifficultyLevel = result.Difficulty;
        session.VisualizationType = result.VisualizationType;
        session.VisualizationPlan = JsonSerializer.Serialize(result);
        session.CurrentState = WorkflowState.VisualizationPlanned;

        return await HandleVisualizationPlannedAsync(session, userMessage, ct);
    }

    // VisualizationPlanned -> ApprovalPending
    private async Task<(string message, string newState)> HandleVisualizationPlannedAsync(LearningSession session, string userMessage, CancellationToken ct)
    {
        VisualizationPlanResult? plan = null;
        if (!string.IsNullOrEmpty(session.VisualizationPlan))
        {
            try { plan = JsonSerializer.Deserialize<VisualizationPlanResult>(session.VisualizationPlan); } catch { }
        }

        var components = plan?.Components ?? session.SelectedComponents?.Split(',').Select(c => c.Trim()).ToList() ?? new List<string>();
        var vizType = plan?.VisualizationType ?? session.VisualizationType ?? "diagram";
        var difficulty = plan?.Difficulty ?? session.DifficultyLevel ?? "beginner";
        var description = plan?.Description ?? $"Visualization of {session.Topic}";

        var message = "**Visualization Plan Ready!**\n\n" +
                      "Here's what I'll create for you:\n\n" +
                      $"- **Topic**: {session.Topic}\n" +
                      $"- **Type**: {vizType}\n" +
                      $"- **Difficulty**: {difficulty}\n" +
                      $"- **Components**: {string.Join(", ", components)}\n" +
                      $"- **Description**: {description}\n\n" +
                      "Would you like to generate this visualization?\n" +
                      "Reply **'yes'** to approve and generate, or **'no'** to go back and change your component selection.";

        session.CurrentState = WorkflowState.ApprovalPending;
        return (message, WorkflowState.ApprovalPending);
    }

    // ApprovalPending -> Completed or back to ComponentSelectionPending
    private async Task<(string message, string newState)> HandleApprovalPendingAsync(LearningSession session, string userMessage, CancellationToken ct)
    {
        var normalized = userMessage.Trim().ToLowerInvariant();
        var isApproved = normalized.StartsWith("yes") || normalized.Contains("approve") || normalized.Contains("generate") || normalized.Contains("go ahead");
        var isDenied = normalized.StartsWith("no") || normalized.Contains("change") || normalized.Contains("back") || normalized.Contains("different");

        if (isDenied && !isApproved)
        {
            session.CurrentState = WorkflowState.ComponentSelectionPending;
            var components = session.SelectedComponents ?? "Overview, Key Concepts, Applications, Examples";
            var availableComponents = components.Split(',').Select(c => c.Trim()).ToList();
            var numberedList = string.Join("\n", availableComponents.Select((c, i) => $"{i + 1}. {c}"));

            return (
                "No problem! Let's revisit the component selection.\n\n" +
                "**Available Components:**\n" +
                $"{numberedList}\n\n" +
                "Please tell me which components to include, difficulty level (beginner/intermediate/expert), and visualization type (diagram/flowchart/text).",
                WorkflowState.ComponentSelectionPending
            );
        }

        if (!isApproved)
        {
            return (
                "Please reply with 'yes' to generate the visualization, or 'no' to change your component selection.",
                WorkflowState.ApprovalPending
            );
        }

        VisualizationPlanResult? plan = null;
        if (!string.IsNullOrEmpty(session.VisualizationPlan))
        {
            try { plan = JsonSerializer.Deserialize<VisualizationPlanResult>(session.VisualizationPlan); } catch { }
        }

        var vizType = plan?.VisualizationType ?? session.VisualizationType ?? "diagram";
        var difficulty = plan?.Difficulty ?? session.DifficultyLevel ?? "beginner";
        var selectedComponents = plan?.Components ?? session.SelectedComponents?.Split(',').Select(c => c.Trim()).ToList() ?? new List<string>();

        var prompt = $"Create a detailed {vizType} visualization explanation for the topic \"{session.Topic}\" at {difficulty} level.\n\n" +
                     $"Components to visualize: {string.Join(", ", selectedComponents)}\n\n" +
                     $"Background context: {session.Explanation}\n\n" +
                     $"Generate a comprehensive, structured {vizType} description that:\n" +
                     "1. Shows relationships between the components\n" +
                     $"2. Uses clear labels and descriptions appropriate for {difficulty} level learners\n" +
                     "3. Explains the flow or structure in detail\n" +
                     $"4. Provides a text-based representation of the {vizType}\n\n" +
                     $"Format the output as a detailed text visualization that could be rendered or used as a reference.\n" +
                     $"Include ASCII art or structured text if appropriate for a {vizType}.";

        var finalOutput = await _ollama.GenerateAsync(prompt, useJsonFormat: false, ct);

        session.FinalOutput = finalOutput;
        session.CurrentState = WorkflowState.Completed;

        var responseMessage = "**Your Visualization is Ready!**\n\n" +
                              $"**Topic**: {session.Topic}\n" +
                              $"**Type**: {vizType} | **Level**: {difficulty}\n\n" +
                              "---\n\n" +
                              $"{finalOutput}\n\n" +
                              "---\n\n" +
                              "Your learning session is complete! Start a new session to explore another topic.";

        return (responseMessage, WorkflowState.Completed);
    }

    private T? ParseJson<T>(string rawResponse) where T : class
    {
        if (string.IsNullOrWhiteSpace(rawResponse)) return null;

        var cleaned = Regex.Replace(rawResponse, @"```(?:json)?\s*", "").Replace("```", "").Trim();

        var jsonStart = cleaned.IndexOf('{');
        var jsonEnd = cleaned.LastIndexOf('}');

        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            cleaned = cleaned[jsonStart..(jsonEnd + 1)];
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            return JsonSerializer.Deserialize<T>(cleaned, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON from LLM response. Raw: {Raw}", rawResponse[..Math.Min(300, rawResponse.Length)]);
            return null;
        }
    }
}
