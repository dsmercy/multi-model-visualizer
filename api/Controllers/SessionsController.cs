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

    // GET /api/sessions  — list recent sessions for the dev user
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SessionSummaryDto>>> ListSessions(
        [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var devUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var sessions = await _db.LearningSessions
            .Where(s => s.UserId == devUserId)
            .OrderByDescending(s => s.UpdatedDate)
            .Take(Math.Min(limit, 50))
            .Select(s => new SessionSummaryDto(
                s.SessionId, s.CurrentState, s.Topic, s.Domain, s.VisualizationType,
                s.CreatedDate, s.UpdatedDate))
            .ToListAsync(ct);
        return Ok(sessions);
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
    public async Task<ActionResult<ApproveResponse>> Approve(Guid id, [FromBody] ApproveRequest? body, CancellationToken ct)
    {
        var session = await _db.LearningSessions.FindAsync(new object[] { id }, ct);
        if (session == null)
            return NotFound(new { error = $"Session {id} not found." });

        if (session.CurrentState != WorkflowState.ApprovalPending)
            return BadRequest(new { error = $"Session must be in ApprovalPending state. Current: {session.CurrentState}" });

        // Override visualization type if the user selected one from the UI
        if (!string.IsNullOrWhiteSpace(body?.VisualizationType))
        {
            session.VisualizationType = body.VisualizationType;
            await _db.SaveChangesAsync(ct);
        }

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

    // POST /api/sessions/{id}/resume  (Phase 4)
    [HttpPost("{id:guid}/resume")]
    public async Task<ActionResult<ResumeResponse>> Resume(Guid id, CancellationToken ct)
    {
        var session = await _db.LearningSessions.FindAsync(new object[] { id }, ct);
        if (session == null) return NotFound();
        try
        {
            var response = await _workflow.ResumeAsync(session, ct);
            return Ok(response);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // POST /api/sessions/{id}/clone  (Phase 4)
    [HttpPost("{id:guid}/clone")]
    public async Task<ActionResult<CloneResponse>> Clone(Guid id, CancellationToken ct)
    {
        var session = await _db.LearningSessions.FindAsync(new object[] { id }, ct);
        if (session == null) return NotFound();
        try
        {
            var response = await _workflow.CloneAsync(session, ct);
            return Ok(response);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // POST /api/sessions/{id}/cancel  (Phase 4)
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<CancelResponse>> Cancel(Guid id, CancellationToken ct)
    {
        var session = await _db.LearningSessions.FindAsync(new object[] { id }, ct);
        if (session == null) return NotFound();
        try
        {
            var response = await _workflow.CancelAsync(session, ct);
            return Ok(response);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // GET /api/sessions/{id}/status  (Phase 4 — concurrency + review info)
    [HttpGet("{id:guid}/status")]
    public async Task<ActionResult<SessionStatusResponse>> GetStatus(Guid id, CancellationToken ct)
    {
        var session = await _db.LearningSessions.FindAsync(new object[] { id }, ct);
        if (session == null) return NotFound();

        var activeJobs = await _db.GenerationJobs
            .CountAsync(j => j.SessionId == id && j.Status == JobStatus.Processing, ct);
        var queuedJobs = await _db.GenerationJobs
            .CountAsync(j => j.SessionId == id && j.Status == JobStatus.Queued, ct);
        var latestReview = await _db.GenerationJobs
            .Where(j => j.SessionId == id && j.ReviewSeverity != null)
            .OrderByDescending(j => j.UpdatedAt)
            .Select(j => j.ReviewSeverity)
            .FirstOrDefaultAsync(ct);

        return Ok(new SessionStatusResponse(id, session.CurrentState, activeJobs, queuedJobs, latestReview, session.ExpiresAt));
    }

    private static SessionDto MapToDto(LearningSession s) => new(
        s.SessionId, s.UserId, s.CurrentState, s.Topic, s.Intent, s.Domain,
        s.SelectedComponents, s.DifficultyLevel, s.VisualizationType,
        s.VisualizationPlan, s.Explanation, s.FinalOutput,
        s.CreatedDate, s.UpdatedDate
    );
}
