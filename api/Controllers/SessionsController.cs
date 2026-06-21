using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MultiModelVisualizer.Api.Data;
using MultiModelVisualizer.Api.Models;
using MultiModelVisualizer.Api.Services;

namespace MultiModelVisualizer.Api.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWorkflowEngine _workflow;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(AppDbContext db, IWorkflowEngine workflow, ILogger<SessionsController> logger)
    {
        _db = db;
        _workflow = workflow;
        _logger = logger;
    }

    // POST /api/sessions
    [HttpPost]
    public async Task<ActionResult<CreateSessionResponse>> CreateSession(CancellationToken ct)
    {
        var session = new LearningSession();
        _db.LearningSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created new session {SessionId}", session.SessionId);

        return Ok(new CreateSessionResponse(session.SessionId, session.CurrentState, session.CreatedDate));
    }

    // GET /api/sessions/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SessionDto>> GetSession(Guid id, CancellationToken ct)
    {
        var session = await _db.LearningSessions.FindAsync(new object[] { id }, ct);
        if (session == null)
            return NotFound(new { error = $"Session {id} not found." });

        return Ok(MapToDto(session));
    }

    // POST /api/sessions/{id}/messages
    [HttpPost("{id:guid}/messages")]
    public async Task<ActionResult<SendMessageResponse>> SendMessage(Guid id, [FromBody] SendMessageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "Message content cannot be empty." });

        var session = await _db.LearningSessions.FindAsync(new object[] { id }, ct);
        if (session == null)
            return NotFound(new { error = $"Session {id} not found." });

        try
        {
            var response = await _workflow.ProcessMessageAsync(session, request.Content, ct);
            return Ok(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Unknown workflow state"))
        {
            _logger.LogError(ex, "Invalid state transition for session {SessionId}", id);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for session {SessionId}", id);
            return StatusCode(500, new { error = "An error occurred processing your message. Please try again." });
        }
    }

    // GET /api/sessions/{id}/events
    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<IEnumerable<SessionEventDto>>> GetEvents(Guid id, CancellationToken ct)
    {
        var sessionExists = await _db.LearningSessions.AnyAsync(s => s.SessionId == id, ct);
        if (!sessionExists)
            return NotFound(new { error = $"Session {id} not found." });

        var events = await _db.LearningSessionEvents
            .Where(e => e.SessionId == id)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new SessionEventDto(
                e.EventId,
                e.SessionId,
                e.PreviousState,
                e.NewState,
                e.Trigger,
                e.EventPayload,
                e.CreatedAt
            ))
            .ToListAsync(ct);

        return Ok(events);
    }

    // POST /api/sessions/{id}/approve
    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<ApproveResponse>> Approve(Guid id, CancellationToken ct)
    {
        var session = await _db.LearningSessions.FindAsync(new object[] { id }, ct);
        if (session == null)
            return NotFound(new { error = $"Session {id} not found." });

        if (session.CurrentState != WorkflowState.ApprovalPending)
            return BadRequest(new { error = $"Session must be in ApprovalPending state. Current: {session.CurrentState}" });

        try
        {
            var response = await _workflow.ApproveAsync(session, ct);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving session {SessionId}", id);
            return StatusCode(500, new { error = "Failed to enqueue generation job." });
        }
    }

    // POST /api/sessions/{id}/reject
    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<SendMessageResponse>> Reject(Guid id, CancellationToken ct)
    {
        var session = await _db.LearningSessions.FindAsync(new object[] { id }, ct);
        if (session == null)
            return NotFound(new { error = $"Session {id} not found." });

        if (session.CurrentState != WorkflowState.ApprovalPending)
            return BadRequest(new { error = $"Session must be in ApprovalPending state. Current: {session.CurrentState}" });

        var response = await _workflow.ProcessMessageAsync(session, "no", ct);
        return Ok(response);
    }

    // POST /api/sessions/{id}/refine
    [HttpPost("{id:guid}/refine")]
    public async Task<ActionResult<SendMessageResponse>> Refine(Guid id, CancellationToken ct)
    {
        var session = await _db.LearningSessions.FindAsync(new object[] { id }, ct);
        if (session == null)
            return NotFound(new { error = $"Session {id} not found." });

        try
        {
            var response = await _workflow.RefineAsync(session, ct);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static SessionDto MapToDto(LearningSession s) => new(
        s.SessionId, s.UserId, s.CurrentState, s.Topic, s.Intent, s.Domain,
        s.SelectedComponents, s.DifficultyLevel, s.VisualizationType,
        s.VisualizationPlan, s.Explanation, s.FinalOutput,
        s.CreatedDate, s.UpdatedDate
    );
}
