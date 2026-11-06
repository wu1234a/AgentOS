using AgentOS.Domain.Rules;

namespace AgentOS.Application.Rules;

/// <summary>Persistence seam for rule management.</summary>
public interface IAutomationRuleRepository
{
    Task<RulePage> ListAsync(RuleListQuery query, CancellationToken cancellationToken);

    Task<RuleDetail?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<AutomationRule?> FindEntityAsync(Guid id, CancellationToken cancellationToken);

    Task AddAsync(AutomationRule rule, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}

public sealed record RuleListQuery(int Page, int PageSize, bool? IsEnabled, string? ProjectSlug);
