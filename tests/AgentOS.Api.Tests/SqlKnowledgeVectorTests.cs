using AgentOS.Api.Knowledge;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlKnowledgeVectorTests
{
    [SkippableFact]
    public async Task Uses_embeddings_to_rank_documents()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION.");
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var service = new KnowledgeService(context, TimeProvider.System, new FakeEmbeddings());
        var marker = Guid.NewGuid().ToString("N")[..8];
        await service.CreateAsync(new KnowledgeDocumentRequest { Title = "cat", Content = $"cat {marker}" }, CancellationToken.None);
        await service.CreateAsync(new KnowledgeDocumentRequest { Title = "dog", Content = $"dog {marker}" }, CancellationToken.None);

        var results = await service.SearchAsync(null, "cat", 10, CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Equal("cat", results[0].Title);
    }

    private sealed class FakeEmbeddings : IEmbeddingClient
    {
        public bool Enabled => true;

        public Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken)
            => Task.FromResult<float[]?>(text.Contains("cat", StringComparison.OrdinalIgnoreCase) ? [1f, 0f] : [0f, 1f]);
    }
}
