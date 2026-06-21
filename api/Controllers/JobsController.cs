using Microsoft.AspNetCore.Mvc;
using MultiModelVisualizer.Api.Models;
using MultiModelVisualizer.Api.Services;

namespace MultiModelVisualizer.Api.Controllers;

[ApiController]
[Route("api/jobs")]
public class JobsController : ControllerBase
{
    private readonly IGenerationJobService _jobs;
    private readonly ILogger<JobsController> _logger;

    public JobsController(IGenerationJobService jobs, ILogger<JobsController> logger)
    {
        _jobs = jobs;
        _logger = logger;
    }

    // GET /api/jobs/{id}/progress  — SSE stream
    [HttpGet("{id:guid}/progress")]
    public async Task GetProgress(Guid id, CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            await foreach (var evt in _jobs.StreamProgressAsync(id, ct))
            {
                var data = System.Text.Json.JsonSerializer.Serialize(evt, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
                await Response.WriteAsync($"data: {data}\n\n", ct);
                await Response.Body.FlushAsync(ct);

                if (evt.Status is JobStatus.Completed or JobStatus.Failed)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSE error for job {JobId}", id);
        }
    }

    // GET /api/jobs/{id}/result
    [HttpGet("{id:guid}/result")]
    public async Task<ActionResult<JobResultResponse>> GetResult(Guid id, CancellationToken ct)
    {
        var job = await _jobs.GetJobAsync(id, ct);
        if (job == null)
            return NotFound(new { error = $"Job {id} not found." });

        return Ok(new JobResultResponse(
            job.JobId, job.SessionId, job.Status,
            job.OutputType, job.OutputUrl, job.OutputContent,
            job.FallbackAttempt, job.Progress,
            job.CreatedAt, job.UpdatedAt
        ));
    }
}
