using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MultiModelVisualizer.Api.Data;
using MultiModelVisualizer.Api.Models;

namespace MultiModelVisualizer.Api.Services;

/// <summary>
/// Background service that periodically checks for sessions stuck in ApprovalPending
/// longer than the configured timeout, transitioning them to ApprovalExpired.
/// Also expires Paused sessions after their timeout.
/// </summary>
public class ApprovalTimeoutService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ApprovalTimeoutService> _logger;

    private TimeSpan ApprovalTimeout => TimeSpan.FromHours(_config.GetValue("WorkflowEngine:ApprovalTimeoutHours", 24));
    private TimeSpan PausedTimeout => TimeSpan.FromDays(_config.GetValue("WorkflowEngine:PausedSessionTimeoutDays", 7));
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    public ApprovalTimeoutService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<ApprovalTimeoutService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ApprovalTimeoutService started (interval: {Interval})", CheckInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckTimeoutsAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Error in ApprovalTimeoutService"); }
            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckTimeoutsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var approvalCutoff = DateTimeOffset.UtcNow - ApprovalTimeout;
        var pendingSessions = await db.LearningSessions
            .Where(s => s.CurrentState == WorkflowState.ApprovalPending && s.UpdatedDate < approvalCutoff)
            .ToListAsync(ct);

        foreach (var session in pendingSessions)
        {
            _logger.LogInformation("Session {SessionId} approval expired (last updated {Updated})", session.SessionId, session.UpdatedDate);
            var prev = session.CurrentState;
            session.CurrentState = WorkflowState.ApprovalExpired;
            session.ExpiresAt = DateTimeOffset.UtcNow;
            session.UpdatedDate = DateTimeOffset.UtcNow;

            db.LearningSessionEvents.Add(new LearningSessionEvent
            {
                SessionId = session.SessionId,
                PreviousState = prev,
                NewState = WorkflowState.ApprovalExpired,
                Trigger = "ApprovalTimeout",
                EventPayload = JsonSerializer.Serialize(new { expiredAt = DateTimeOffset.UtcNow, timeoutHours = ApprovalTimeout.TotalHours })
            });
        }

        var pausedCutoff = DateTimeOffset.UtcNow - PausedTimeout;
        var pausedSessions = await db.LearningSessions
            .Where(s => s.CurrentState == WorkflowState.Paused && s.UpdatedDate < pausedCutoff)
            .ToListAsync(ct);

        foreach (var session in pausedSessions)
        {
            _logger.LogInformation("Session {SessionId} paused timeout → Cancelled", session.SessionId);
            var prev = session.CurrentState;
            session.CurrentState = WorkflowState.Cancelled;
            session.CancelledAt = DateTimeOffset.UtcNow;
            session.UpdatedDate = DateTimeOffset.UtcNow;

            db.LearningSessionEvents.Add(new LearningSessionEvent
            {
                SessionId = session.SessionId,
                PreviousState = prev,
                NewState = WorkflowState.Cancelled,
                Trigger = "PausedTimeout",
                EventPayload = JsonSerializer.Serialize(new { cancelledAt = DateTimeOffset.UtcNow })
            });
        }

        if (pendingSessions.Count > 0 || pausedSessions.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
