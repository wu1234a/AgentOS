using System.Text.Encodings.Web;
using System.Text.Json;
using AgentOS.Domain.Knowledge;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Api.Knowledge;

public sealed class KnowledgeDocumentRequest
{
    public Guid? ProjectId { get; init; }
    public string? Title { get; init; }
    public string? Content { get; init; }
    public string[]? Tags { get; init; }
    public string? Source { get; init; }
}

public sealed record KnowledgeDocumentView(
    Guid Id,
    Guid? ProjectId,
    string Title,
    string Content,
    string[] Tags,
    string Source,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? Snippet = null);

public sealed class KnowledgeService(AgentOSDbContext dbContext, TimeProvider timeProvider, IEmbeddingClient? embeddings = null)
{
    public async Task<(KnowledgeDocumentView? Document, string? Error)> CreateAsync(
        KnowledgeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var content = request.Content?.Trim() ?? string.Empty;
        if (title.Length is < 1 or > 200) return (null, "invalid-title");
        if (content.Length is < 1 or > 200_000) return (null, "invalid-content");
        var source = string.IsNullOrWhiteSpace(request.Source) ? "manual" : request.Source.Trim();
        if (source.Length > 100) return (null, "invalid-source");
        if (request.ProjectId is Guid projectId
            && !await dbContext.Projects.AnyAsync(project => project.Id == projectId, cancellationToken))
        {
            return (null, "project-not-found");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var document = new KnowledgeDocument
        {
            Id = Guid.CreateVersion7(),
            ProjectId = request.ProjectId,
            Title = title,
            Content = content,
            TagsJson = JsonSerializer.Serialize(request.Tags ?? [], TagsOptions),
            Source = source,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        if (embeddings is { Enabled: true })
        {
            var vector = await embeddings.EmbedAsync(
                $"{title}\n{content}\n{string.Join(' ', request.Tags ?? [])}",
                cancellationToken);
            if (vector is not null)
            {
                document.EmbeddingJson = JsonSerializer.Serialize(vector);
            }
        }

        dbContext.KnowledgeDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (ToView(document, null), null);
    }

    public async Task<IReadOnlyList<KnowledgeDocumentView>> SearchAsync(
        Guid? projectId, string? query, int limit, CancellationToken cancellationToken)
    {
        var documents = dbContext.KnowledgeDocuments.AsNoTracking();
        if (projectId is Guid id) documents = documents.Where(document => document.ProjectId == id);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            documents = documents.Where(document =>
                document.Title.Contains(term) || document.Content.Contains(term) || document.TagsJson.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query) && embeddings is { Enabled: true })
        {
            var queryVector = await embeddings.EmbedAsync(query.Trim(), cancellationToken);
            if (queryVector is not null)
            {
                var candidates = await documents.OrderByDescending(document => document.UpdatedAtUtc)
                    .Take(500).ToListAsync(cancellationToken);
                return candidates
                    .Select(document => new
                    {
                        Document = document,
                        Score = Similarity(queryVector, ParseVector(document.EmbeddingJson))
                    })
                    .OrderByDescending(row => row.Score)
                    .ThenByDescending(row => row.Document.UpdatedAtUtc)
                    .Take(Math.Clamp(limit, 1, 50))
                    .Select(row => ToView(row.Document, query))
                    .ToList();
            }
        }

        var rows = await documents.OrderByDescending(document => document.UpdatedAtUtc)
            .Take(Math.Clamp(limit, 1, 50)).ToListAsync(cancellationToken);
        return rows.Select(document => ToView(document, query)).ToList();
    }

    public async Task<KnowledgeDocumentView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await dbContext.KnowledgeDocuments.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return document is null ? null : ToView(document, null);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await dbContext.KnowledgeDocuments.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (document is null) return false;
        dbContext.KnowledgeDocuments.Remove(document);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static readonly JsonSerializerOptions TagsOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static float[]? ParseVector(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<float[]>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double Similarity(float[] query, float[]? candidate)
    {
        if (candidate is null || candidate.Length != query.Length || query.Length == 0)
        {
            return 0;
        }

        double dot = 0, queryNorm = 0, candidateNorm = 0;
        for (var index = 0; index < query.Length; index++)
        {
            dot += query[index] * candidate[index];
            queryNorm += query[index] * query[index];
            candidateNorm += candidate[index] * candidate[index];
        }

        return queryNorm == 0 || candidateNorm == 0
            ? 0
            : dot / (Math.Sqrt(queryNorm) * Math.Sqrt(candidateNorm));
    }

    private static KnowledgeDocumentView ToView(KnowledgeDocument document, string? query)
    {
        var tags = JsonSerializer.Deserialize<string[]>(document.TagsJson) ?? [];
        return new KnowledgeDocumentView(document.Id, document.ProjectId, document.Title, document.Content, tags,
            document.Source, document.CreatedAtUtc, document.UpdatedAtUtc, BuildSnippet(document.Content, query));
    }

    private static string? BuildSnippet(string content, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var index = content.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        var start = Math.Max(0, index - 80);
        return content.Substring(start, Math.Min(240, content.Length - start));
    }
}
