using System.Text;
using System.Text.Json;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace MultiModelVisualizer.Api.Services;

public class KnowledgeService : IKnowledgeService
{
    private const string CollectionName = "knowledge";
    private const int EmbeddingDimensions = 768;

    private readonly QdrantClient _qdrant;
    private readonly HttpClient _ollamaHttp;
    private readonly IConfiguration _config;
    private readonly ILogger<KnowledgeService> _logger;

    public KnowledgeService(QdrantClient qdrant, HttpClient ollamaHttp, IConfiguration config, ILogger<KnowledgeService> logger)
    {
        _qdrant = qdrant;
        _ollamaHttp = ollamaHttp;
        _config = config;
        _logger = logger;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var ollamaUrl = _config["AI:OllamaBaseUrl"] ?? "http://localhost:11434";
        var payload = new { model = "nomic-embed-text", prompt = text };
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await _ollamaHttp.PostAsync($"{ollamaUrl}/api/embeddings", content, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var embeddingArray = doc.RootElement.GetProperty("embedding");

        var result = new float[EmbeddingDimensions];
        int i = 0;
        foreach (var el in embeddingArray.EnumerateArray())
            result[i++] = el.GetSingle();

        return result;
    }

    public async Task<bool> EnsureCollectionExistsAsync(CancellationToken ct = default)
    {
        try
        {
            var collections = await _qdrant.ListCollectionsAsync(ct);
            if (collections.Any(c => c == CollectionName))
                return true;

            await _qdrant.CreateCollectionAsync(CollectionName,
                new VectorParams { Size = EmbeddingDimensions, Distance = Distance.Cosine },
                cancellationToken: ct);

            _logger.LogInformation("Created Qdrant collection '{Collection}'", CollectionName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure Qdrant collection exists");
            return false;
        }
    }

    public async Task<long> GetCollectionPointCountAsync(CancellationToken ct = default)
    {
        try
        {
            var info = await _qdrant.GetCollectionInfoAsync(CollectionName, ct);
            return (long)info.PointsCount;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<KnowledgeSearchResult> SearchAsync(
        string query,
        string? domain = null,
        int topK = 5,
        double threshold = 0.65,
        CancellationToken ct = default)
    {
        try
        {
            var queryVector = await EmbedAsync(query, ct);

            Filter? filter = null;
            if (!string.IsNullOrEmpty(domain))
            {
                filter = new Filter
                {
                    Must = { new Condition { Field = new FieldCondition { Key = "domain", Match = new Match { Text = domain } } } }
                };
            }

            var results = await _qdrant.SearchAsync(
                CollectionName,
                queryVector,
                filter: filter,
                limit: (ulong)topK,
                scoreThreshold: (float)threshold,
                payloadSelector: true,
                cancellationToken: ct);

            var chunks = results.Select(r => new KnowledgeChunk(
                ChunkId: r.Id.Uuid,
                Content: r.Payload.TryGetValue("content", out var c) ? c.StringValue : "",
                Source: r.Payload.TryGetValue("source", out var s) ? s.StringValue : "unknown",
                Domain: r.Payload.TryGetValue("domain", out var d) ? d.StringValue : "general",
                Topic: r.Payload.TryGetValue("topic", out var t) ? t.StringValue : null,
                Score: r.Score
            )).ToList();

            // Expand search with lower threshold if no hits
            if (chunks.Count == 0 && threshold > 0.50)
            {
                _logger.LogInformation("No chunks found at threshold {Threshold}, expanding to 0.50", threshold);
                return await SearchAsync(query, domain, topK, 0.50, ct);
            }

            return new KnowledgeSearchResult(
                chunks,
                chunks.Count > 0,
                chunks.Count > 0 ? chunks.Max(c => c.Score) : 0.0
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant search failed for query: {Query}", query);
            return new KnowledgeSearchResult(new List<KnowledgeChunk>(), false, 0.0);
        }
    }

    public async Task<IngestResult> IngestDocumentAsync(
        string content,
        string source,
        string domain,
        string? topic = null,
        CancellationToken ct = default)
    {
        await EnsureCollectionExistsAsync(ct);

        var chunks = ChunkText(content, chunkSize: 512, overlap: 64);
        var points = new List<PointStruct>();

        foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
        {
            var embedding = await EmbedAsync(chunk, ct);
            var pointId = Guid.NewGuid();

            var payload = new Dictionary<string, Value>
            {
                ["content"] = new Value { StringValue = chunk },
                ["source"] = new Value { StringValue = source },
                ["domain"] = new Value { StringValue = domain },
                ["chunk_index"] = new Value { IntegerValue = index },
            };

            if (topic != null)
                payload["topic"] = new Value { StringValue = topic };

            points.Add(new PointStruct
            {
                Id = new PointId { Uuid = pointId.ToString() },
                Vectors = new Vectors { Vector = new Vector { Data = { embedding } } },
                Payload = { payload }
            });
        }

        await _qdrant.UpsertAsync(CollectionName, points, cancellationToken: ct);

        _logger.LogInformation("Ingested {Count} chunks from '{Source}' into collection '{Collection}'", points.Count, source, CollectionName);

        return new IngestResult(points.Count, CollectionName, domain, source);
    }

    private static List<string> ChunkText(string text, int chunkSize = 512, int overlap = 64)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();

        int start = 0;
        while (start < words.Length)
        {
            int end = Math.Min(start + chunkSize, words.Length);
            chunks.Add(string.Join(" ", words[start..end]));
            start += chunkSize - overlap;
            if (start >= words.Length) break;
        }

        return chunks.Count > 0 ? chunks : new List<string> { text };
    }
}
