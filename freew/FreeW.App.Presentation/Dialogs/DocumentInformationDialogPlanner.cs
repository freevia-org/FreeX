using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum StatisticsDialogDepth
{
    Compact,
    Detailed
}

public sealed record StatisticsDialogRow(
    string Key,
    string Label,
    string Value,
    bool StartsNewSection = false);

public sealed record StatisticsDialogPlan(
    string Title,
    IReadOnlyList<StatisticsDialogRow> Rows);

public static class StatisticsDialogPlanner
{
    public const string IncludeNotesLabel = "Include footnotes and endnotes";

    public static StatisticsDialogPlan Build(
        TextDocument document,
        bool includeNotes,
        StatisticsDialogDepth depth,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Build(DocumentStatistics.Compute(document, includeNotes), depth, culture);
    }

    public static StatisticsDialogPlan Build(
        DocumentStatistics statistics,
        StatisticsDialogDepth depth,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        var compactRows = new List<StatisticsDialogRow>
        {
            Row("words", "Words", statistics.Words, culture),
            Row("characters-no-spaces", "Characters (no spaces)", statistics.CharactersWithoutSpaces, culture),
            Row("characters-with-spaces", "Characters (with spaces)", statistics.CharactersWithSpaces, culture),
            Row("paragraphs", "Paragraphs", statistics.Paragraphs, culture),
            Row("lines", "Lines", statistics.Lines, culture)
        };

        if (depth == StatisticsDialogDepth.Compact)
            return new StatisticsDialogPlan("Word Count", compactRows);

        return new StatisticsDialogPlan(
            "Word Count",
            [
                compactRows[0],
                compactRows[2],
                compactRows[1],
                compactRows[3],
                compactRows[4],
                Row("sentences", "Sentences", statistics.Sentences, culture),
                new StatisticsDialogRow(
                    "reading-time",
                    "Reading time",
                    FormatReadingTime(statistics.ReadingTimeMinutes),
                    StartsNewSection: true),
                new StatisticsDialogRow(
                    "words-per-sentence",
                    "Words per sentence",
                    statistics.AverageWordsPerSentence.ToString("0.0", culture)),
                new StatisticsDialogRow(
                    "readability",
                    "Readability (Flesch)",
                    $"{statistics.FleschReadingEase.ToString("0.0", culture)} \u2014 {DescribeEase(statistics.FleschReadingEase)}")
            ]);
    }

    private static StatisticsDialogRow Row(string key, string label, int value, CultureInfo culture) =>
        new(key, label, value.ToString("N0", culture));

    private static string FormatReadingTime(int minutes) => minutes switch
    {
        <= 0 => "less than a minute",
        1 => "1 minute",
        _ => $"{minutes} minutes"
    };

    private static string DescribeEase(double score) => score switch
    {
        >= 90 => "very easy",
        >= 70 => "easy",
        >= 60 => "plain English",
        >= 50 => "fairly difficult",
        >= 30 => "difficult",
        _ => "very difficult"
    };
}

public sealed record AccessibilityDialogGroupPlan(
    AccessibilitySeverity Severity,
    string Heading,
    IReadOnlyList<string> IssueLines,
    string AccentHex);

public sealed record AccessibilityDialogPlan(
    string Title,
    string Summary,
    IReadOnlyList<AccessibilityDialogGroupPlan> Groups)
{
    public bool IsClean => Groups.Count == 0;
}

public static class AccessibilityReportDialogPlanner
{
    public static AccessibilityDialogPlan Build(AccessibilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var groups = new[]
        {
            BuildGroup(report, AccessibilitySeverity.Error, "Errors"),
            BuildGroup(report, AccessibilitySeverity.Warning, "Warnings"),
            BuildGroup(report, AccessibilitySeverity.Tip, "Tips")
        }
        .Where(group => group is not null)
        .Select(group => group!)
        .ToArray();

        var summary = report.IsClean
            ? "No accessibility issues found."
            : $"{report.ErrorCount} error(s), {report.WarningCount} warning(s), {report.TipCount} tip(s).";

        return new AccessibilityDialogPlan("Accessibility Checker", summary, groups);
    }

    private static AccessibilityDialogGroupPlan? BuildGroup(
        AccessibilityReport report,
        AccessibilitySeverity severity,
        string heading)
    {
        var issueLines = report.Issues
            .Where(issue => issue.Severity == severity)
            .Select(issue => $"\u2022  {issue.Message}")
            .ToArray();

        return issueLines.Length == 0
            ? null
            : new AccessibilityDialogGroupPlan(
                severity,
                $"{heading} ({issueLines.Length})",
                issueLines,
                severity switch
                {
                    AccessibilitySeverity.Error => "#C00000",
                    AccessibilitySeverity.Warning => "#B86A00",
                    _ => "#404040",
                });
    }
}
