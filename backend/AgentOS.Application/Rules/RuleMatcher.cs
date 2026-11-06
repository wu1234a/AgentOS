using AgentOS.Domain.Events;

namespace AgentOS.Application.Rules;

/// <summary>
/// Deterministic rule matching and ordering. Ordering is the documented contract:
/// project-scoped rules before global rules, then higher priority, then oldest first,
/// then id ascending — so the same rule set always produces the same task order.
/// </summary>
public static class RuleMatcher
{
    public static bool IsMatch(RuleSnapshot rule, Event evt)
    {
        if (rule.ProjectId is not null && rule.ProjectId != evt.ProjectId)
        {
            return false;
        }

        if (!RulePattern.IsMatch(rule.SourcePattern, evt.Source))
        {
            return false;
        }

        if (!RulePattern.IsMatch(rule.EventTypePattern, evt.EventType))
        {
            return false;
        }

        return rule.Conditions.Matches(evt.SubjectType, evt.Priority);
    }

    /// <summary>Matching rules in the order tasks must be created.</summary>
    public static IReadOnlyList<RuleSnapshot> Match(IEnumerable<RuleSnapshot> rules, Event evt)
        => Order(rules.Where(rule => IsMatch(rule, evt)));

    public static IReadOnlyList<RuleSnapshot> Order(IEnumerable<RuleSnapshot> rules)
        => rules
            .OrderByDescending(rule => rule.ProjectId is not null)
            .ThenByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.CreatedAtUtc)
            .ThenBy(rule => rule.Id)
            .ToArray();
}
