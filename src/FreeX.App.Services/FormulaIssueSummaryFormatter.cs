using Free.Shared.Localization;

namespace FreeX.App.Services;

/// <summary>
/// R129-model-avalonia-info-formula-issues-1: shared "N issues found" wording for File &gt; Info's
/// summary rows. Both shells must say the same thing for the same workbook state -- previously only
/// the WPF host's <see cref="BackstageInfoPlanner"/> had this formatting (inline, private), and the
/// Avalonia/macOS shell's <see cref="WorkbookInfoDisplayPlanner"/> had no formula-issue/circular-
/// reference field at all. Factored out here so both surfaces (and any future one) share one
/// implementation instead of drifting.
/// </summary>
public static class FormulaIssueSummaryFormatter
{
    public static string Format(int issueCount, string emptySummaryKey, ResourceKeyTextResolver strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return issueCount == 0
            ? strings.Get(emptySummaryKey)
            : issueCount == 1
                ? strings.Get("Backstage_Info_OneIssueFound")
                : strings.Format("Backstage_Info_MultipleIssuesFound", issueCount);
    }
}
