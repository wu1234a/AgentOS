using System.Text.Json;
using AgentOS.Application.Events;
using AgentOS.Domain.Events;
using AgentOS.Domain.Projects;
using AgentOS.Infrastructure.Persistence;
using AgentOS.Infrastructure.Projects;
using AgentOS.Infrastructure.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentOS.Api.Tests;

/// <summary>
/// AOS-003 acceptance tests that need a real SQL Server: the unique index, concurrent
/// submissions, and the upgrade migration over legacy duplicate rows.
/// </summary>
[Collection("SqlServer")]
public sealed class SqlEventDeduplicationTests
{
    private const string PreviousMigration = "20261105202604_AddProcessedAtUtc";
    private const string DedupeIndexName = "UX_Events_Source_DedupeKey";

    [SkippableFact]
    public async Task Unique_index_rejects_a_second_row_with_the_same_source_and_dedupe_key()
    {
        var connectionString = RequireConnectionString();
        await using var dbContext = new AgentOSDbContext(Options(connectionString));
        await dbContext.Database.MigrateAsync();

        var source = UniqueSource();
        const string dedupeKey = "unique-index";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await InsertEventAsync(connection, Guid.CreateVersion7(), source, dedupeKey, """{"a":1}""");

        var duplicate = await Assert.ThrowsAsync<SqlException>(() =>
            InsertEventAsync(connection, Guid.CreateVersion7(), source, dedupeKey, """{"a":1}"""));

        Assert.True(
            duplicate.Number is 2601 or 2627,
            $"expected a unique key violation, got SQL error {duplicate.Number}");
        Assert.Equal(1, await CountUniqueIndexAsync(connection));
    }

    [SkippableFact]
    public async Task Concurrent_submissions_produce_exactly_one_event_row()
    {
        var connectionString = RequireConnectionString();
        await using (var setup = new AgentOSDbContext(Options(connectionString)))
        {
            await setup.Database.MigrateAsync();
        }

        var source = UniqueSource();
        const string dedupeKey = "concurrent";
        const int attempts = 8;

        var tasks = Enumerable.Range(0, attempts)
            .Select(index => Task.Run(() => InsertAsync(connectionString, source, dedupeKey, index)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(result => result.Outcome == EventWriteOutcome.Inserted));
        Assert.Equal(attempts - 1, results.Count(result => result.Outcome == EventWriteOutcome.DuplicateKey));

        var winner = results.Single(result => result.Outcome == EventWriteOutcome.Inserted);
        Assert.All(
            results.Where(result => result.Outcome == EventWriteOutcome.DuplicateKey),
            result => Assert.Equal(winner.Existing!.Id, result.Existing!.Id));

        Assert.Equal(1, await CountRowsAsync(connectionString, source, dedupeKey));
    }

    [SkippableFact]
    public async Task Service_reports_duplicate_and_conflict_against_the_real_index()
    {
        var connectionString = RequireConnectionString();
        await using var dbContext = new AgentOSDbContext(Options(connectionString));
        await dbContext.Database.MigrateAsync();

        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            Name = "Dedupe Integration",
            Slug = $"dedupe-{Guid.NewGuid():N}",
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var dedupeKey = $"integration:{Guid.NewGuid():N}";

        var created = await service.CreateAsync(Request(project.Slug, dedupeKey, """{"a":1}"""), CancellationToken.None);
        Assert.Equal(CreateEventOutcome.Created, created.Outcome);

        var duplicate = await service.CreateAsync(Request(project.Slug, dedupeKey, """{"a":1}"""), CancellationToken.None);
        Assert.Equal(CreateEventOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(created.EventId, duplicate.EventId);

        var conflict = await service.CreateAsync(Request(project.Slug, dedupeKey, """{"a":2}"""), CancellationToken.None);
        Assert.Equal(CreateEventOutcome.Conflict, conflict.Outcome);
        Assert.Equal(created.EventId, conflict.ExistingEventId);

        Assert.Equal(1, await dbContext.Events.CountAsync(evt => evt.DedupeKey == dedupeKey));
    }

    [SkippableFact]
    public async Task Legacy_row_without_hash_is_backfilled_on_the_first_duplicate()
    {
        var connectionString = RequireConnectionString();
        await using var dbContext = new AgentOSDbContext(Options(connectionString));
        await dbContext.Database.MigrateAsync();

        // A row written before AOS-003: no PayloadHash, payload stored in its original formatting.
        var legacyId = Guid.CreateVersion7();
        var dedupeKey = $"legacy-backfill-{Guid.NewGuid():N}";

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await InsertEventAsync(connection, legacyId, "integration", dedupeKey, """{ "b": 2, "a": 1 }""");
        }

        var service = CreateService(dbContext);
        var result = await service.CreateAsync(Request(null, dedupeKey, """{"a":1,"b":2}"""), CancellationToken.None);

        Assert.Equal(CreateEventOutcome.Duplicate, result.Outcome);
        Assert.Equal(legacyId, result.EventId);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "SELECT [PayloadHash] FROM [Events] WHERE [Id] = @id;",
                connection);
            command.Parameters.AddWithValue("@id", legacyId);

            var hash = await command.ExecuteScalarAsync();
            Assert.IsType<string>(hash);

            var text = (string)hash!;
            Assert.Equal(EventPayloadHasher.HashLength, text.Length);
            Assert.Matches("^[0-9a-f]{64}$", text);
        }

        Assert.Equal(1, await dbContext.Events.CountAsync(evt => evt.Source == "integration" && evt.DedupeKey == dedupeKey));
    }

    [SkippableFact]
    public async Task Upgrade_migration_keeps_legacy_duplicate_rows_and_still_enforces_uniqueness()
    {
        var connectionString = RequireConnectionString();
        await using var dbContext = new AgentOSDbContext(Options(connectionString));

        var migrator = dbContext.GetService<IMigrator>();

        // Rewind to the AOS-002 schema, seed data that predates the unique index, then upgrade.
        await migrator.MigrateAsync(PreviousMigration, CancellationToken.None);

        var source = UniqueSource();
        const string dedupeKey = "legacy-duplicate";

        var firstId = Guid.CreateVersion7();
        var secondId = Guid.CreateVersion7();
        var thirdId = Guid.CreateVersion7();
        var emptyKeyId = Guid.CreateVersion7();
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await InsertLegacyEventAsync(connection, firstId, source, dedupeKey, baseTime);
            await InsertLegacyEventAsync(connection, secondId, source, dedupeKey, baseTime.AddSeconds(1));
            await InsertLegacyEventAsync(connection, thirdId, source, dedupeKey, baseTime.AddSeconds(2));
            await InsertLegacyEventAsync(connection, emptyKeyId, source, string.Empty, baseTime.AddSeconds(3));
        }

        await migrator.MigrateAsync();

        var rows = new List<(Guid Id, string DedupeKey, string? PayloadHash)>();
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();

            await using (var command = new SqlCommand(
                "SELECT [Id], [DedupeKey], [PayloadHash] FROM [Events] WHERE [Source] = @source ORDER BY [Id];",
                connection))
            {
                command.Parameters.AddWithValue("@source", source);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    rows.Add((
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2)));
                }
            }

            Assert.Equal(4, rows.Count);
            Assert.Equal(4, rows.Select(row => row.DedupeKey).Distinct(StringComparer.Ordinal).Count());

            // The earliest row keeps the key; the other two are preserved under suffixed keys.
            Assert.Equal(dedupeKey, rows.Single(row => row.Id == firstId).DedupeKey);
            Assert.StartsWith($"{dedupeKey}~legacy-dup-", rows.Single(row => row.Id == secondId).DedupeKey);
            Assert.StartsWith($"{dedupeKey}~legacy-dup-", rows.Single(row => row.Id == thirdId).DedupeKey);

            // Rows with an empty dedupe key are repaired rather than deleted.
            // CONVERT(nvarchar(36), uniqueidentifier) renders the GUID in upper case.
            Assert.Equal(
                $"legacy-{emptyKeyId:D}",
                rows.Single(row => row.Id == emptyKeyId).DedupeKey,
                ignoreCase: true);

            // Legacy rows keep a NULL hash; the application backfills it lazily.
            Assert.All(rows, row => Assert.Null(row.PayloadHash));

            Assert.Equal(1, await CountUniqueIndexAsync(connection));

            var violation = await Assert.ThrowsAsync<SqlException>(() =>
                InsertEventAsync(connection, Guid.CreateVersion7(), source, dedupeKey, """{"a":1}"""));

            Assert.True(violation.Number is 2601 or 2627, $"expected unique violation, got {violation.Number}");
        }
    }

    [SkippableFact]
    public async Task Upgrade_migration_handles_long_and_colliding_legacy_keys()
    {
        var connectionString = RequireConnectionString();
        await using var dbContext = new AgentOSDbContext(Options(connectionString));

        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, CancellationToken.None);

        var longSource = UniqueSource();
        var collisionSource = UniqueSource();

        // 200 emoji = 400 UTF-16 code units: the whole nvarchar(400) column. A character-based
        // LEFT(...,351) would keep 351 characters (702 units) and overflow the suffix.
        var longKey = string.Concat(Enumerable.Repeat("😀", 200));
        const string collisionKey = "collide-key";

        var baseTime = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var longFirstId = Guid.CreateVersion7();
        var longSecondId = Guid.CreateVersion7();
        var collideFirstId = Guid.CreateVersion7();
        var collideSecondId = Guid.CreateVersion7();
        var collideLiteralId = Guid.CreateVersion7();

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();

            await InsertLegacyEventAsync(connection, longFirstId, longSource, longKey, baseTime);
            await InsertLegacyEventAsync(connection, longSecondId, longSource, longKey, baseTime.AddSeconds(1));

            await InsertLegacyEventAsync(connection, collideFirstId, collisionSource, collisionKey, baseTime);
            await InsertLegacyEventAsync(connection, collideSecondId, collisionSource, collisionKey, baseTime.AddSeconds(1));

            // A row that already uses the exact key the first rename pass would choose.
            await InsertLegacyEventAsync(
                connection,
                collideLiteralId,
                collisionSource,
                $"{collisionKey}~legacy-dup-{collideSecondId:D}",
                baseTime.AddSeconds(2));
        }

        await migrator.MigrateAsync();

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();

            var longRows = await ReadDedupeKeysAsync(connection, longSource);
            Assert.Equal(2, longRows.Count);
            Assert.Equal(2, longRows.Select(row => row.DedupeKey).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(longKey, longRows.Single(row => row.Id == longFirstId).DedupeKey);
            Assert.Equal(
                $"legacy-dup-{longSecondId:D}",
                longRows.Single(row => row.Id == longSecondId).DedupeKey,
                ignoreCase: true);

            var collideRows = await ReadDedupeKeysAsync(connection, collisionSource);
            Assert.Equal(3, collideRows.Count);
            Assert.Equal(3, collideRows.Select(row => row.DedupeKey).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(collisionKey, collideRows.Single(row => row.Id == collideFirstId).DedupeKey);

            Assert.Equal(1, await CountUniqueIndexAsync(connection));
        }
    }

    private static async Task<List<(Guid Id, string DedupeKey)>> ReadDedupeKeysAsync(
        SqlConnection connection,
        string source)
    {
        await using var command = new SqlCommand(
            "SELECT [Id], [DedupeKey] FROM [Events] WHERE [Source] = @source ORDER BY [Id];",
            connection);
        command.Parameters.AddWithValue("@source", source);

        var rows = new List<(Guid Id, string DedupeKey)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<EventWriteResult> InsertAsync(
        string connectionString,
        string source,
        string dedupeKey,
        int index)
    {
        var options = Options(connectionString);
        await using var dbContext = new AgentOSDbContext(options);
        var repository = new EventRepository(dbContext);
        var now = DateTime.UtcNow;

        return await repository.TryAddAsync(
            new Event
            {
                Id = Guid.CreateVersion7(),
                Source = source,
                EventType = "integration.event.created",
                SchemaVersion = "1.0",
                DedupeKey = dedupeKey,
                Summary = $"concurrent {index}",
                PayloadJson = "{}",
                PayloadHash = EventPayloadHasher.ComputeHash("{}"),
                Priority = "normal",
                Status = EventStatus.Received,
                OccurredAtUtc = now,
                ReceivedAtUtc = now
            },
            CancellationToken.None);
    }

    private static EventService CreateService(AgentOSDbContext dbContext) => new(
        new CreateEventValidator(),
        new EventPayloadHasher(),
        new DuplicateEventLogSampler(TimeProvider.System),
        new EventRepository(dbContext),
        new ProjectLookup(dbContext),
        TimeProvider.System,
        NullLogger<EventService>.Instance);

    private static CreateEventRequest Request(string? projectSlug, string dedupeKey, string payload) => new()
    {
        SchemaVersion = "1.0",
        Source = "integration",
        EventType = "integration.event.created",
        Project = projectSlug,
        Summary = "integration event",
        DedupeKey = dedupeKey,
        OccurredAtUtc = "2026-11-05T00:00:00Z",
        Payload = JsonSerializer.Deserialize<JsonElement>(payload)
    };

    private static async Task InsertEventAsync(
        SqlConnection connection,
        Guid id,
        string source,
        string dedupeKey,
        string payloadJson)
    {
        const string sql = """
            INSERT INTO [Events]
                ([Id], [ProjectId], [Source], [EventType], [SchemaVersion], [ExternalEventId],
                 [DedupeKey], [Summary], [ResourceUrl], [PayloadJson], [PayloadHash],
                 [SubjectType], [SubjectId], [Priority], [Status], [ProcessedAtUtc],
                 [OccurredAtUtc], [ReceivedAtUtc], [CorrelationId])
            VALUES
                (@id, NULL, @source, N'integration.event.created', N'1.0', NULL,
                 @dedupeKey, N'raw insert', NULL, @payload, NULL,
                 NULL, NULL, N'normal', N'Received', NULL,
                 @now, @now, NULL);
            """;

        var now = DateTime.UtcNow;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@source", source);
        command.Parameters.AddWithValue("@dedupeKey", dedupeKey);
        command.Parameters.AddWithValue("@payload", payloadJson);
        command.Parameters.AddWithValue("@now", now);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertLegacyEventAsync(
        SqlConnection connection,
        Guid id,
        string source,
        string dedupeKey,
        DateTime receivedAtUtc)
    {
        const string sql = """
            INSERT INTO [Events]
                ([Id], [ProjectId], [Source], [EventType], [SchemaVersion], [ExternalEventId],
                 [DedupeKey], [Summary], [ResourceUrl], [PayloadJson],
                 [SubjectType], [SubjectId], [Priority], [Status], [ProcessedAtUtc],
                 [OccurredAtUtc], [ReceivedAtUtc], [CorrelationId])
            VALUES
                (@id, NULL, @source, N'integration.event.created', N'1.0', NULL,
                 @dedupeKey, N'legacy insert', NULL, N'{"a":1}',
                 NULL, NULL, N'normal', N'Received', NULL,
                 @now, @now, NULL);
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@source", source);
        command.Parameters.AddWithValue("@dedupeKey", dedupeKey);
        command.Parameters.AddWithValue("@now", receivedAtUtc);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountUniqueIndexAsync(SqlConnection connection)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE [name] = @name AND [object_id] = OBJECT_ID(N'dbo.Events');
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@name", DedupeIndexName);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> CountRowsAsync(string connectionString, string source, string dedupeKey)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM [Events] WHERE [Source] = @source AND [DedupeKey] = @dedupeKey;",
            connection);
        command.Parameters.AddWithValue("@source", source);
        command.Parameters.AddWithValue("@dedupeKey", dedupeKey);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static string RequireConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTOS_SQL_TEST_CONNECTION");
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            "Set AGENTOS_SQL_TEST_CONNECTION to a disposable database to run the SQL deduplication tests.");
        return connectionString!;
    }

    private static DbContextOptions<AgentOSDbContext> Options(string connectionString)
        => new DbContextOptionsBuilder<AgentOSDbContext>()
            .UseSqlServer(connectionString)
            .Options;

    private static string UniqueSource() => $"it-{Guid.NewGuid():N}";
}
