using ProcessBooster.Core.Models;

namespace ProcessBooster.Core.Services;

/// <summary>Pure rule-to-process matching. No OS calls, so fully unit-testable.</summary>
public static class RuleMatcher
{
    /// <summary>True if an enabled, actionable rule targets the given process name.</summary>
    public static bool Matches(ProcessRule rule, string processName)
    {
        if (!rule.Enabled || !rule.HasAnyAction) return false;
        return rule.NormalizedMatch.Length > 0 &&
               rule.NormalizedMatch == ProcessRule.Normalize(processName);
    }

    /// <summary>The first enabled rule that targets this process, or null.</summary>
    public static ProcessRule? FirstMatch(IEnumerable<ProcessRule> rules, string processName)
        => rules.FirstOrDefault(r => Matches(r, processName));
}
