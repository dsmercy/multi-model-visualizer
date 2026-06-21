using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MultiModelVisualizer.Api.Data;
using MultiModelVisualizer.Api.Models;
using MultiModelVisualizer.Api.Services;

namespace MultiModelVisualizer.Api.Controllers;

[ApiController]
[Route("api/admin/knowledge")]
public class KnowledgeController : ControllerBase
{
    private readonly IKnowledgeService _knowledge;
    private readonly AppDbContext _db;
    private readonly ILogger<KnowledgeController> _logger;

    public KnowledgeController(IKnowledgeService knowledge, AppDbContext db, ILogger<KnowledgeController> logger)
    {
        _knowledge = knowledge;
        _db = db;
        _logger = logger;
    }

    [HttpPost("ingest")]
    public async Task<ActionResult<IngestResponse>> Ingest([FromBody] IngestRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "Content is required" });

        if (string.IsNullOrWhiteSpace(request.Source))
            return BadRequest(new { error = "Source is required" });

        if (string.IsNullOrWhiteSpace(request.Domain))
            return BadRequest(new { error = "Domain is required" });

        try
        {
            var result = await _knowledge.IngestDocumentAsync(
                request.Content, request.Source, request.Domain, request.Topic, ct);

            return Ok(new IngestResponse(
                result.ChunksStored,
                result.CollectionName,
                result.Domain,
                result.Source,
                DateTimeOffset.UtcNow
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed for source '{Source}'", request.Source);
            return StatusCode(500, new { error = "Ingestion failed", detail = ex.Message });
        }
    }

    [HttpGet("status")]
    public async Task<ActionResult<KnowledgeStatusResponse>> Status(CancellationToken ct)
    {
        var exists = await _knowledge.EnsureCollectionExistsAsync(ct);
        var count = exists ? await _knowledge.GetCollectionPointCountAsync(ct) : 0;

        return Ok(new KnowledgeStatusResponse(
            "knowledge",
            count,
            exists,
            DateTimeOffset.UtcNow
        ));
    }
}

[ApiController]
[Route("api/sessions/{id:guid}/citations")]
public class SessionCitationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public SessionCitationsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<SessionCitationsResponse>> GetCitations(Guid id, CancellationToken ct)
    {
        var session = await _db.LearningSessions.FindAsync(new object[] { id }, ct);
        if (session == null) return NotFound();

        var citations = new List<CitationDto>();

        if (!string.IsNullOrEmpty(session.Citations))
        {
            try
            {
                using var doc = JsonDocument.Parse(session.Citations);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    citations.Add(new CitationDto(
                        ChunkId: el.TryGetProperty("chunkId", out var cid) ? cid.GetString() ?? "" : "",
                        Source: el.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "",
                        Domain: el.TryGetProperty("domain", out var dom) ? dom.GetString() ?? "" : "",
                        Topic: el.TryGetProperty("topic", out var top) && top.ValueKind != JsonValueKind.Null ? top.GetString() : null,
                        Score: el.TryGetProperty("score", out var sc) ? sc.GetDouble() : 0.0,
                        Excerpt: el.TryGetProperty("excerpt", out var ex) ? ex.GetString() ?? "" : ""
                    ));
                }
            }
            catch { /* return empty list if JSON is malformed */ }
        }

        return Ok(new SessionCitationsResponse(
            id,
            session.ComponentSourceStrategy,
            citations
        ));
    }
}
