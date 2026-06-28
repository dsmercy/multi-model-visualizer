using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MultiModelVisualizer.Api.Models;

[Table("generation_jobs")]
public class GenerationJob
{
    [Key]
    [Column("job_id")]
    public Guid JobId { get; set; } = Guid.NewGuid();

    [Column("session_id")]
    public Guid SessionId { get; set; }

    [Column("status")]
    public string Status { get; set; } = JobStatus.Queued;

    [Column("visualization_type")]
    public string? VisualizationType { get; set; }

    [Column("fallback_attempt")]
    public int FallbackAttempt { get; set; } = 0;

    [Column("output_type")]
    public string? OutputType { get; set; }

    [Column("output_url")]
    public string? OutputUrl { get; set; }

    [Column("output_content")]
    public string? OutputContent { get; set; }

    [Column("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }

    [Column("error_code")]
    public string? ErrorCode { get; set; }

    [Column("progress")]
    public int Progress { get; set; } = 0;

    // Phase 4
    [Column("retry_count")]
    public int RetryCount { get; set; } = 0;

    [Column("next_retry_at")]
    public DateTimeOffset? NextRetryAt { get; set; }

    [Column("review_status")]
    public string? ReviewStatus { get; set; }

    [Column("review_severity")]
    public string? ReviewSeverity { get; set; }

    [Column("review_notes")]
    public string? ReviewNotes { get; set; }

    [Column("queue_name")]
    public string QueueName { get; set; } = "generation.diagram";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public LearningSession? Session { get; set; }
}

public static class JobStatus
{
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string FallbackGeneration = "FallbackGeneration";
    // Phase 4
    public const string Retrying = "Retrying";
    public const string RetryExhausted = "RetryExhausted";
    public const string Reviewing = "Reviewing";
    public const string Reviewed = "Reviewed";
}

public static class QueueNames
{
    public const string ThreeD = "generation.3d";
    public const string Video = "generation.video";
    public const string Diagram = "generation.diagram";
    public const string Text = "generation.text";
    public const string Fallback = "generation.fallback";
    public const string Dlq = "generation.dlq";

    public static string ForVisualizationType(string vizType) => vizType switch
    {
        "3d" or "3d_animation" => ThreeD,
        "video" => Video,
        "text" => Text,
        "auto" => Fallback,  // auto: use fallback queue, worker starts at level 0
        _ => Diagram,
    };
}

public static class ReviewSeverity
{
    public const string Minor = "Minor";
    public const string Major = "Major";
    public const string Critical = "Critical";
}
