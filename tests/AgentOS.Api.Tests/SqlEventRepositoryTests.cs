using AgentOS.Application.Events;
using AgentOS.Domain.Events;
using AgentOS.Domain.Projects;
using AgentOS.Infrastructure.Persistence;
using AgentOS.Infrastructure.Projects;
using AgentOS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Api.Tests;

/// <summary>
/// Opt-in integration test against a real SQL Server. Set
/// <c>AGENTOS_SQL_TEST_CONNECTION</c> to a disposable database connection string to run it.
/// </summary>
[Collection("SqlServer")]
public sealed class SqlEventRepositoryTests
{
    [SkippableFact]
    public async Task Repository_persists_and_queries_events_against_sql_server()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            "Set AGENTOS_SQL_TEST_CONNECTION to run the SQL repository integration test.");

        var options = new DbContextOptionsBuilder<AgentOSDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var dbContext = new AgentOSDbContext(options);
        await dbContext.Database.MigrateAsync();

        var slug = $"integration-{Guid.NewGuid():N}";
        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = "Integration Test",
            Slug = slug,
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();

        var receivedAtUtc = DateTime.UtcNow;
        var entity = new Event
        {
            Id = Guid.CreateVersion7(),
            ProjectId = project.Id,
            Source = "integration",
            EventType = "integration.event.created",
            SchemaVersion = "1.0",
            DedupeKey = $"{slug}:1",
            Summary = "integration event",
            PayloadJson = "{\"ok\":true}",
            SubjectType = "test",
            SubjectId = "1",
            Priority = "normal",
            Status = EventStatus.Received,
            OccurredAtUtc = receivedAtUtc,
            ReceivedAtUtc = receivedAtUtc
        };

        var repository = new EventRepository(dbContext);
        var writeResult = await repository.TryAddAsync(entity, CancellationToken.None);
        Assert.Equal(EventWriteOutcome.Inserted, writeResult.Outcome);

        var loaded = await repository.GetByIdAsync(entity.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(slug, loaded!.Project?.Slug);
        Assert.Equal("{\"ok\":true}", loaded.PayloadJson);

        var page = await repository.ListAsync(
            new EventListQuery(1, 10, "integration", "integration.event.created", "Received", slug),
            CancellationToken.None);

        Assert.Equal(1, page.Total);
        Assert.Single(page.Items);

        var lookup = new ProjectLookup(dbContext);
        var found = await lookup.FindBySlugAsync(slug, CancellationToken.None);
        Assert.NotNull(found);
    }
}
