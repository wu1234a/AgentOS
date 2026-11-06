using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AgentOS.Domain.Skills;

namespace AgentOS.Application.Skills;

/// <summary>Skill version registry: validation, file storage and enable/disable lifecycle.</summary>
public sealed class SkillService(ISkillRepository repository, ISkillStore store, TimeProvider timeProvider)
{
    private static readonly Regex CodePattern = new(
        "^[a-z0-9][a-z0-9-]{2,119}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VersionPattern = new(
        "^[0-9A-Za-z][0-9A-Za-z._-]{0,39}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Task<(IReadOnlyList<SkillView> Items, int Total)> ListAsync(
        Guid? projectId,
        string? code,
        bool includeDisabled,
        int limit,
        int offset,
        CancellationToken cancellationToken)
        => repository.ListAsync(projectId, code, includeDisabled, limit, offset, cancellationToken);

    public Task<SkillView?> GetAsync(Guid skillId, CancellationToken cancellationToken)
        => repository.GetViewAsync(skillId, cancellationToken);

    public async Task<string?> GetContentAsync(Guid skillId, CancellationToken cancellationToken)
    {
        var skill = await repository.GetTrackedAsync(skillId, cancellationToken);
        return skill is null ? null : await store.ReadAsync(skill.RelativePath, cancellationToken);
    }

    public async Task<SkillResult> CreateAsync(
        CreateSkillRequest request,
        CancellationToken cancellationToken)
    {
        if (!store.IsConfigured)
        {
            return SkillResult.StorageUnavailable();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var code = NormalizeCode(request.Code, errors);
        var version = NormalizeVersion(request.Version, errors);

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description is { Length: > 1000 })
        {
            errors["description"] = ["description must be 1000 characters or fewer."];
        }

        var content = request.Content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            errors["content"] = ["content is required."];
        }

        if (errors.Count > 0)
        {
            return SkillResult.Invalid(errors);
        }

        if (await repository.VersionExistsAsync(request.ProjectId, code!, version!, cancellationToken))
        {
            return SkillResult.Conflict("skill-version-conflict");
        }

        StoredSkillContent stored;

        try
        {
            stored = await store.SaveAsync(request.ProjectId, code!, version!, content, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return SkillResult.Invalid(new Dictionary<string, string[]>
            {
                ["content"] = [exception.Message]
            });
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var skill = new Skill
        {
            Id = Guid.CreateVersion7(),
            ProjectId = request.ProjectId,
            Code = code!,
            Version = version!,
            RelativePath = stored.RelativePath,
            ContentHash = stored.ContentHash,
            Description = description,
            IsEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        if (!await repository.TryAddAsync(skill, cancellationToken))
        {
            await store.DeleteAsync(stored.RelativePath, cancellationToken);
            return SkillResult.Conflict("skill-version-conflict");
        }

        return SkillResult.Ok((await repository.GetViewAsync(skill.Id, cancellationToken))!);
    }

    public async Task<SkillResult> UpdateAsync(
        Guid skillId,
        UpdateSkillRequest request,
        CancellationToken cancellationToken)
    {
        var skill = await repository.GetTrackedAsync(skillId, cancellationToken);
        if (skill is null)
        {
            return SkillResult.NotFound();
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description is { Length: > 1000 })
        {
            return SkillResult.Invalid(new Dictionary<string, string[]>
            {
                ["description"] = ["description must be 1000 characters or fewer."]
            });
        }

        skill.Description = description;
        skill.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await repository.SaveAsync(cancellationToken);

        return SkillResult.Ok((await repository.GetViewAsync(skillId, cancellationToken))!);
    }

    public Task<SkillResult> EnableAsync(Guid skillId, CancellationToken cancellationToken)
        => SetEnabledAsync(skillId, true, cancellationToken);

    public Task<SkillResult> DisableAsync(Guid skillId, CancellationToken cancellationToken)
        => SetEnabledAsync(skillId, false, cancellationToken);

    private async Task<SkillResult> SetEnabledAsync(
        Guid skillId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var skill = await repository.GetTrackedAsync(skillId, cancellationToken);
        if (skill is null)
        {
            return SkillResult.NotFound();
        }

        if (skill.IsEnabled != enabled)
        {
            skill.IsEnabled = enabled;
            skill.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await repository.SaveAsync(cancellationToken);
        }

        return SkillResult.Ok((await repository.GetViewAsync(skillId, cancellationToken))!);
    }

    private static string? NormalizeCode(string? code, Dictionary<string, string[]> errors)
    {
        var normalized = code?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !CodePattern.IsMatch(normalized))
        {
            errors["code"] = ["code must be a lowercase slug of 3-120 characters."];
            return null;
        }

        return normalized;
    }

    private static string? NormalizeVersion(string? version, Dictionary<string, string[]> errors)
    {
        var normalized = version?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || !VersionPattern.IsMatch(normalized))
        {
            errors["version"] = ["version must match [0-9A-Za-z][0-9A-Za-z._-]{0,39}."];
            return null;
        }

        return normalized;
    }
}
