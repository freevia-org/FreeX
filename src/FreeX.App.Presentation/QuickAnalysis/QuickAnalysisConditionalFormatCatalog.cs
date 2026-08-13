using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

internal sealed record QuickAnalysisConditionalFormatDescriptor(
    QuickAnalysisFormatKind FormatKind,
    QuickAnalysisConditionalFormatCommand Command,
    ConditionalFormatPreset Preset,
    string DialogTitle,
    QuickAnalysisConditionalFormatDialogSeed DialogSeed);

/// <summary>
/// Single source of truth for the conditional-format intent shared by Quick Analysis catalog entries,
/// dialog-backed hosts, and direct-apply hosts.
/// </summary>
internal static class QuickAnalysisConditionalFormatCatalog
{
    private static readonly QuickAnalysisConditionalFormatDescriptor[] Entries =
    [
        Entry(QuickAnalysisFormatKind.DataBars, QuickAnalysisConditionalFormatCommand.DataBar,
            ConditionalFormatPreset.DataBar, "Data Bar", new(CfRuleType.DataBar)),
        Entry(QuickAnalysisFormatKind.ColorScale, QuickAnalysisConditionalFormatCommand.ColorScale,
            ConditionalFormatPreset.ColorScale, "Color Scale", new(CfRuleType.ColorScale)),
        Entry(QuickAnalysisFormatKind.IconSet, QuickAnalysisConditionalFormatCommand.IconSet,
            ConditionalFormatPreset.IconSet, "Icon Set", new(CfRuleType.IconSet)),
        Entry(QuickAnalysisFormatKind.GreaterThan, QuickAnalysisConditionalFormatCommand.GreaterThan,
            ConditionalFormatPreset.HighlightGreaterThan, "Greater Than",
            new(CfRuleType.CellValue, CfOperator.GreaterThan)),
        Entry(QuickAnalysisFormatKind.LessThan, QuickAnalysisConditionalFormatCommand.LessThan,
            ConditionalFormatPreset.HighlightLessThan, "Less Than",
            new(CfRuleType.CellValue, CfOperator.LessThan)),
        Entry(QuickAnalysisFormatKind.Between, QuickAnalysisConditionalFormatCommand.Between,
            ConditionalFormatPreset.HighlightBetween, "Between",
            new(CfRuleType.CellValue, CfOperator.Between)),
        Entry(QuickAnalysisFormatKind.EqualTo, QuickAnalysisConditionalFormatCommand.EqualTo,
            ConditionalFormatPreset.HighlightEqualTo, "Equal To",
            new(CfRuleType.CellValue, CfOperator.Equal)),
        Entry(QuickAnalysisFormatKind.TextContains, QuickAnalysisConditionalFormatCommand.TextContains,
            ConditionalFormatPreset.HighlightTextContains, "Text Contains",
            new(CfRuleType.ContainsText, Text: string.Empty)),
        Entry(QuickAnalysisFormatKind.DateOccurring, QuickAnalysisConditionalFormatCommand.DateOccurring,
            ConditionalFormatPreset.HighlightDateOccurring, "Date Occurring",
            new(CfRuleType.DateOccurring, DateOccurringPeriod: "Today")),
        Entry(QuickAnalysisFormatKind.DuplicateValues, QuickAnalysisConditionalFormatCommand.DuplicateValues,
            ConditionalFormatPreset.HighlightDuplicateValues, "Duplicate Values",
            new(CfRuleType.DuplicateValues)),
        Entry(QuickAnalysisFormatKind.Top10, QuickAnalysisConditionalFormatCommand.Top10Items,
            ConditionalFormatPreset.Top10, "Top 10 Items", new(CfRuleType.Top10)),
        Entry(QuickAnalysisFormatKind.Top10Percent, QuickAnalysisConditionalFormatCommand.Top10Percent,
            ConditionalFormatPreset.Top10Percent, "Top 10%",
            new(CfRuleType.Top10, TopBottomPercent: true)),
        Entry(QuickAnalysisFormatKind.Bottom10, QuickAnalysisConditionalFormatCommand.Bottom10Items,
            ConditionalFormatPreset.Bottom10Items, "Bottom 10 Items",
            new(CfRuleType.Top10, IsTop: false)),
        Entry(QuickAnalysisFormatKind.Bottom10Percent, QuickAnalysisConditionalFormatCommand.Bottom10Percent,
            ConditionalFormatPreset.Bottom10Percent, "Bottom 10%",
            new(CfRuleType.Top10, TopBottomPercent: true, IsTop: false)),
        Entry(QuickAnalysisFormatKind.AboveAverage, QuickAnalysisConditionalFormatCommand.AboveAverage,
            ConditionalFormatPreset.AboveAverage, "Above Average", new(CfRuleType.AboveAverage)),
        Entry(QuickAnalysisFormatKind.BelowAverage, QuickAnalysisConditionalFormatCommand.BelowAverage,
            ConditionalFormatPreset.BelowAverage, "Below Average",
            new(CfRuleType.AboveAverage, IsTop: false)),
    ];

    private static readonly IReadOnlyDictionary<QuickAnalysisFormatKind, QuickAnalysisConditionalFormatDescriptor>
        EntriesByFormatKind = Entries.ToDictionary(entry => entry.FormatKind);

    private static readonly IReadOnlyDictionary<QuickAnalysisConditionalFormatCommand, QuickAnalysisConditionalFormatDescriptor>
        EntriesByCommand = Entries.ToDictionary(entry => entry.Command);

    public static QuickAnalysisConditionalFormatDescriptor ForFormatKind(QuickAnalysisFormatKind formatKind) =>
        EntriesByFormatKind.TryGetValue(formatKind, out var entry)
            ? entry
            : throw new ArgumentOutOfRangeException(nameof(formatKind), formatKind, "Unknown Quick Analysis format kind.");

    public static QuickAnalysisConditionalFormatDescriptor ForCommand(
        QuickAnalysisConditionalFormatCommand command) =>
        TryForCommand(command, out var entry)
            ? entry
            : throw new ArgumentOutOfRangeException(
                nameof(command),
                command,
                "Unsupported conditional-format command.");

    public static bool TryForCommand(
        QuickAnalysisConditionalFormatCommand command,
        out QuickAnalysisConditionalFormatDescriptor descriptor) =>
        EntriesByCommand.TryGetValue(command, out descriptor!);

    private static QuickAnalysisConditionalFormatDescriptor Entry(
        QuickAnalysisFormatKind formatKind,
        QuickAnalysisConditionalFormatCommand command,
        ConditionalFormatPreset preset,
        string dialogTitle,
        QuickAnalysisConditionalFormatDialogSeed dialogSeed) =>
        new(formatKind, command, preset, dialogTitle, dialogSeed);
}
