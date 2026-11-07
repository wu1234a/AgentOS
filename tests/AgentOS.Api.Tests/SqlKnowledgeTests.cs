using AgentOS.Api.Knowledge;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Api.Tests;

[Collection("SqlServer")]
public sealed class SqlKnowledgeTests
{
    [SkippableFact]
    public async Task Creates_and_searches_documents()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "Set AGENTOS_SQL_TEST_CONNECTION.");
        await using var context = new AgentOSDbContext(
            new DbContextOptionsBuilder<AgentOSDbContext>().UseSqlServer(connectionString).Options);
        await context.Database.MigrateAsync();

        var service = new KnowledgeService(context, TimeProvider.System);
        var marker = $"needle-{Guid.NewGuid():N}";
        var (document, error) = await service.CreateAsync(new KnowledgeDocumentRequest
        {
            Title = "Runbook",
            Content = $"Deploy steps include {marker} and rollback.",
            Tags = ["ops"]
        }, CancellationToken.None);

        Assert.Null(error);
        Assert.NotNull(document);

        var results = await service.SearchAsync(null, marker, 10, CancellationToken.None);
        var found = Assert.Single(results);
        Assert.Equal(document!.Id, found.Id);
        Assert.Contains(marker, found.Snippet);
    }
}
