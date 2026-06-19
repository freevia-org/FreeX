using System.Globalization;

using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// Portable carrier for the totals &amp; layout-display options the PivotTable Options dialog edits.
/// </summary>
public sealed record PivotOptionsValues(
    bool ShowRowGrandTotals,
    bool ShowColumnGrandTotals,
    bool ShowSubtotals,
    PivotSubtotalPlacement SubtotalPlacement,
    PivotReportLayout ReportLayout,
    int CompactRowLabelIndent,
    bool RepeatItemLabels,
    bool BlankLineAfterItems,
    bool MergeAndCenterLabels);

/// <summary>
/// Portable, UI-free planning for the PivotTable Options dialog: the report-layout and subtotal-placement
/// display catalogs (English labels), capturing the current option values off a <see cref="PivotTableModel"/>,
/// validating the compact-row-label indent box, and applying the dialog's collected values back onto a
/// values record. Single-sourced here so every desktop host shares identical behavior; building the command
/// and running it stays with each shell's command glue (the host passes these values to
/// <c>ConfigurePivotTableOptionsCommand</c>, leaving its other cache/print/alt-text options untouched).
/// </summary>
public static class PivotOptionsPlanner
{
    public const int MinCompactRowLabelIndent = 0;
    public const int MaxCompactRowLabelIndent = 15;

    public const string CompactIndentRangeMessage =
        "Enter a compact-form row-label indent between 0 and 15.";

    /// <summary>Report layouts in display order, with the English label the dialog shows.</summary>
    public static readonly IReadOnlyList<(string Label, PivotReportLayout Value)> ReportLayouts =
    [
        ("Compact Form", PivotReportLayout.Compact),
        ("Outline Form", PivotReportLayout.Outline),
        ("Tabular Form", PivotReportLayout.Tabular),
    ];

    /// <summary>Subtotal placements in display order, with the English label the dialog shows.</summary>
    public static readonly IReadOnlyList<(string Label, PivotSubtotalPlacement Value)> SubtotalPlacements =
    [
        ("Show subtotals at bottom of group", PivotSubtotalPlacement.Bottom),
        ("Show subtotals at top of group", PivotSubtotalPlacement.Top),
    ];

    public static int FindReportLayoutIndex(PivotReportLayout layout)
    {
        for (var index = 0; index < ReportLayouts.Count; index++)
        {
            if (ReportLayouts[index].Value == layout)
                return index;
        }

        return 0;
    }

    public static int FindSubtotalPlacementIndex(PivotSubtotalPlacement placement)
    {
        for (var index = 0; index < SubtotalPlacements.Count; index++)
        {
            if (SubtotalPlacements[index].Value == placement)
                return index;
        }

        return 0;
    }

    public static PivotReportLayout ReportLayoutFromIndex(int selectedIndex) =>
        ReportLayouts[Math.Max(0, Math.Min(selectedIndex, ReportLayouts.Count - 1))].Value;

    public static PivotSubtotalPlacement SubtotalPlacementFromIndex(int selectedIndex) =>
        SubtotalPlacements[Math.Max(0, Math.Min(selectedIndex, SubtotalPlacements.Count - 1))].Value;

    /// <summary>Snapshots the current totals/layout-display option values off the pivot.</summary>
    public static PivotOptionsValues Capture(PivotTableModel pivotTable)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        return new PivotOptionsValues(
            pivotTable.ShowRowGrandTotals,
            pivotTable.ShowColumnGrandTotals,
            pivotTable.ShowSubtotals,
            pivotTable.SubtotalPlacement,
            pivotTable.ReportLayout,
            pivotTable.CompactRowLabelIndent,
            pivotTable.RepeatItemLabels,
            pivotTable.BlankLineAfterItems,
            pivotTable.MergeAndCenterLabels);
    }

    /// <summary>Validates the compact-form indent box; parses it on success.</summary>
    public static bool TryParseCompactRowLabelIndent(string? text, out int indent, out string? error)
    {
        error = null;
        if (int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out indent) &&
            indent is >= MinCompactRowLabelIndent and <= MaxCompactRowLabelIndent)
        {
            return true;
        }

        indent = MinCompactRowLabelIndent;
        error = CompactIndentRangeMessage;
        return false;
    }

    /// <summary>The compact-form indent box text for the current value.</summary>
    public static string CompactRowLabelIndentText(int indent) =>
        indent.ToString(CultureInfo.CurrentCulture);

    /// <summary>Builds the resulting option values from the dialog's collected input.</summary>
    public static PivotOptionsValues CreateResult(
        bool showRowGrandTotals,
        bool showColumnGrandTotals,
        bool showSubtotals,
        int subtotalPlacementIndex,
        int reportLayoutIndex,
        int compactRowLabelIndent,
        bool repeatItemLabels,
        bool blankLineAfterItems,
        bool mergeAndCenterLabels) =>
        new(
            showRowGrandTotals,
            showColumnGrandTotals,
            showSubtotals,
            SubtotalPlacementFromIndex(subtotalPlacementIndex),
            ReportLayoutFromIndex(reportLayoutIndex),
            Math.Clamp(compactRowLabelIndent, MinCompactRowLabelIndent, MaxCompactRowLabelIndent),
            repeatItemLabels,
            blankLineAfterItems,
            mergeAndCenterLabels);
}
