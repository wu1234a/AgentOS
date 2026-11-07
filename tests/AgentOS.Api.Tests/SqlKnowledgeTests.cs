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

        var (tagged, _) = await service.CreateAsync(new KnowledgeDocumentRequest
        {
            Title = "部署手册",
            Content = "生产部署步骤",
            Tags = ["部署"]
        }, CancellationToken.None);
        Assert.NotNull(tagged);
        Assert.Contains(await service.SearchAsync(null, "部署", 10, CancellationToken.None), item => item.Id == tagged!.Id);

        var (_, sourceError) = await service.CreateAsync(new KnowledgeDocumentRequest
        {
            Title = "bad source",
            Content = "content",
            Source = new string('x', 101)
        }, CancellationToken.None);
        Assert.Equal("invalid-source", sourceError);
    }
}
