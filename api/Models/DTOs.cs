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

// Phase 2 DTOs
public record ApproveRequest(string? VisualizationType = null);
public record ApproveResponse(Guid JobId, string Status, Guid SessionId);

public record JobProgressEvent(Guid JobId, string Status, int Progress, string? OutputType = null, string? OutputUrl = null, string? ErrorCode = null);

public record JobResultResponse(
    Guid JobId,
    Guid SessionId,
    string Status,
    string? OutputType,
    string? OutputUrl,
    string? OutputContent,
    int FallbackAttempt,
    int Progress,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record GenerationContract(
    Guid JobId,
    Guid SessionId,
    string VisualizationType,
    string Concept,
    string Domain,
    List<string> Components,
    GenerationSettings Settings,
    int FallbackAttempt
);

public record GenerationSettings(
    bool Narration,
    bool Labels,
    string Difficulty,
    string DetailLevel,
    string RenderingStyle
);

public record PythonGenerationResult(
    Guid JobId,
    string Status,
    string? OutputType,
    string? OutputUrl,
    string? OutputContent,
    string? ErrorCode,
    bool Retryable,
    PythonGenerationMetadata? Metadata
);

public record PythonGenerationMetadata(
    List<string>? ComponentsCovered,
    double GenerationDurationSeconds
);

// Phase 4 DTOs
public record ResumeResponse(Guid SessionId, string NewState, string Message);
public record CloneResponse(Guid NewSessionId, Guid ClonedFromSessionId, string CurrentState);
public record CancelResponse(Guid SessionId, string NewState, DateTimeOffset CancelledAt);
public record SessionStatusResponse(
    Guid SessionId,
    string CurrentState,
    int ActiveJobs,
    int QueuedJobs,
    string? ReviewSeverity,
    DateTimeOffset? ExpiresAt
);

public record SessionSummaryDto(
    Guid SessionId,
    string CurrentState,
    string? Topic,
    string? Domain,
    string? VisualizationType,
    DateTimeOffset CreatedDate,
    DateTimeOffset UpdatedDate
);

// Phase 3 DTOs
public record IngestRequest(string Content, string Source, string Domain, string? Topic = null);
public record IngestResponse(int ChunksStored, string CollectionName, string Domain, string Source, DateTimeOffset IngestedAt);
public record KnowledgeStatusResponse(string CollectionName, long PointCount, bool CollectionExists, DateTimeOffset CheckedAt);
public record CitationDto(string ChunkId, string Source, string Domain, string? Topic, double Score, string Excerpt);
public record SessionCitationsResponse(Guid SessionId, string ComponentSourceStrategy, List<CitationDto> Citations);

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
