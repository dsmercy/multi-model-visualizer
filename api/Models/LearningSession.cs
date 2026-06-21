using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MultiModelVisualizer.Api.Models;

[Table("learning_sessions")]
public class LearningSession
{
    [Key]
    [Column("session_id")]
    public Guid SessionId { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public Guid UserId { get; set; } = new Guid("00000000-0000-0000-0000-000000000001");

    [Required]
    [Column("current_state")]
    public string CurrentState { get; set; } = WorkflowState.Created;

    [Column("topic")]
    public string? Topic { get; set; }

    [Column("intent")]
    public string? Intent { get; set; }

    [Column("domain")]
    public string? Domain { get; set; }

    [Column("selected_components")]
    public string? SelectedComponents { get; set; }

    [Column("difficulty_level")]
    public string? DifficultyLevel { get; set; }

    [Column("visualization_type")]
    public string? VisualizationType { get; set; }

    [Column("visualization_plan")]
    public string? VisualizationPlan { get; set; }

    [Column("explanation")]
    public string? Explanation { get; set; }

    [Column("final_output")]
    public string? FinalOutput { get; set; }

    [Column("created_date")]
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;

    [Column("updated_date")]
    public DateTimeOffset UpdatedDate { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<LearningSessionEvent> Events { get; set; } = new List<LearningSessionEvent>();
}

public static class WorkflowState
{
    public const string Created = "Created";
    public const string IntentAnalyzed = "IntentAnalyzed";
    public const string DomainClassified = "DomainClassified";
    public const string ConceptExplained = "ConceptExplained";
    public const string ComponentSelectionPending = "ComponentSelectionPending";
    public const string VisualizationPlanned = "VisualizationPlanned";
    public const string ApprovalPending = "ApprovalPending";
    public const string Completed = "Completed";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Created, IntentAnalyzed, DomainClassified, ConceptExplained,
        ComponentSelectionPending, VisualizationPlanned, ApprovalPending, Completed
    };
}
