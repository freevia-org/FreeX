using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Portable selection of the conditional-format rules that apply to a single cell, in the order the
/// engine evaluates them. Rules whose <see cref="ConditionalFormat.AppliesTo"/> range does not
/// contain the cell are excluded; the remainder are ordered by ascending
/// <see cref="ConditionalFormat.Priority"/> (Excel convention: lower number = higher precedence),
/// with insertion order as a stable tie-break. A rule marked
/// <see cref="ConditionalFormat.StopIfTrue"/> terminates evaluation of all lower-priority rules for
/// that cell — callers stop applying further rules once a stop-if-true rule has been honored. The
/// engine already bakes the final per-cell result into the <see cref="DisplayCell"/>; this helper
/// captures the same ordering contract so the render layer reasons about it without a running UI.
/// </summary>
public static class ConditionalFormatRulePlanner
{
    /// <summary>
    /// Return the rules applicable to <paramref name="address"/>, ordered by priority then insertion
    /// index. Does not itself short-circuit on stop-if-true (callers evaluate each rule's condition
    /// and honor <see cref="ShouldStopAfter"/>), so the full applicable set is observable for tests.
    /// </summary>
    public static IReadOnlyList<ConditionalFormat> OrderApplicableRules(
        IReadOnlyList<ConditionalFormat> rules,
        CellAddress address)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var applicable = new List<(ConditionalFormat Rule, int Index)>();
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule.AppliesTo.Contains(address))
                applicable.Add((rule, i));
        }

        applicable.Sort((left, right) =>
        {
            var byPriority = left.Rule.Priority.CompareTo(right.Rule.Priority);
            return byPriority != 0 ? byPriority : left.Index.CompareTo(right.Index);
        });

        var ordered = new ConditionalFormat[applicable.Count];
        for (var i = 0; i < applicable.Count; i++)
            ordered[i] = applicable[i].Rule;
        return ordered;
    }

    /// <summary>
    /// True when, after a rule whose condition matched, no lower-priority rules should be evaluated
    /// for the cell. This is the engine's stop-if-true semantics.
    /// </summary>
    public static bool ShouldStopAfter(ConditionalFormat matchedRule)
    {
        ArgumentNullException.ThrowIfNull(matchedRule);
        return matchedRule.StopIfTrue;
    }
}
