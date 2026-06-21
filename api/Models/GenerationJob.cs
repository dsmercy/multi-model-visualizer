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
}
