using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MultiModelVisualizer.Api.Models;

[Table("learning_session_events")]
public class LearningSessionEvent
{
    [Key]
    [Column("event_id")]
    public Guid EventId { get; set; } = Guid.NewGuid();

    [Required]
    [Column("session_id")]
    public Guid SessionId { get; set; }

    [Column("previous_state")]
    public string? PreviousState { get; set; }

    [Required]
    [Column("new_state")]
    public string NewState { get; set; } = string.Empty;

    [Column("trigger")]
    public string? Trigger { get; set; }

    [Column("event_payload")]
    public string? EventPayload { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [ForeignKey(nameof(SessionId))]
    public LearningSession? Session { get; set; }
}
