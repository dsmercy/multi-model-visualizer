namespace MultiModelVisualizer.Api.Services;

public record KnowledgeChunk(
    string ChunkId,
    string Content,
    string Source,
    string Domain,
    string? Topic,
    double Score
);

public record KnowledgeSearchResult(
    List<KnowledgeChunk> Chunks,
    bool FoundRelevantContent,
    double MaxScore
);

public record IngestResult(
    int ChunksStored,
    string CollectionName,
    string Domain,
    string Source
);

public interface IKnowledgeService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<KnowledgeSearchResult> SearchAsync(string query, string? domain = null, int topK = 5, double threshold = 0.65, CancellationToken ct = default);
    Task<IngestResult> IngestDocumentAsync(string content, string source, string domain, string? topic = null, CancellationToken ct = default);
    Task<bool> EnsureCollectionExistsAsync(CancellationToken ct = default);
    Task<long> GetCollectionPointCountAsync(CancellationToken ct = default);
}
