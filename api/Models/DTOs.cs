namespace MultiModelVisualizer.Api.Models;

public record CreateSessionResponse(
    Guid SessionId,
    string CurrentState,
    DateTimeOffset CreatedAt
);

public record SendMessageRequest(string Content);

public record SendMessageResponse(
    string Message,
    string NewState,
    Guid SessionId
);

public record SessionDto(
    Guid SessionId,
    Guid UserId,
    string CurrentState,
    string? Topic,
    string? Intent,
    string? Domain,
    string? SelectedComponents,
    string? DifficultyLevel,
    string? VisualizationType,
    string? VisualizationPlan,
    string? Explanation,
    string? FinalOutput,
    DateTimeOffset CreatedDate,
    DateTimeOffset UpdatedDate
);

public record SessionEventDto(
    Guid EventId,
    Guid SessionId,
    string? PreviousState,
    string NewState,
    string? Trigger,
    string? EventPayload,
    DateTimeOffset CreatedAt
);

public record HealthResponse(
    string Status,
    string Version,
    DateTimeOffset Timestamp
);

// Internal DTOs for LLM responses
public record IntentAnalysisResult(string Intent, string Topic, double Confidence);
public record DomainClassificationResult(string Domain, string Subdomain, double Confidence);
public record ConceptExplanationResult(string Explanation, List<string> Components, string Summary);
public record VisualizationPlanResult(
    string VisualizationType,
    List<string> Components,
    string Difficulty,
    string Description
);
