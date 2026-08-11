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
/// Portable carrier for the full PivotTable Options dialog result. Desktop shells own widgets and dialog
/// chrome; this type owns the normalized option values handed to PivotTable option commands.
/// </summary>
public sealed record PivotOptionsDialogValues(
    bool ShowRowGrandTotals,
    bool ShowColumnGrandTotals,
    bool ShowSubtotals,
    PivotSubtotalPlacement SubtotalPlacement,
    bool RepeatItemLabels,
    bool BlankLineAfterItems,
    string StyleName,
    bool ShowRowHeaders,
    bool ShowColumnHeaders,
    bool ShowRowStripes,
    bool ShowColumnStripes,
    PivotReportLayout ReportLayout,
    string? EmptyValueText = null,
    bool RefreshOnOpen = false,
    bool SaveSourceData = true,
    bool EnableRefresh = true,
    bool PreserveSourceSortFilter = true,
    int? MissingItemsLimit = null,
    bool PrintTitles = false,
    bool PrintExpandCollapseButtons = false,
    string? AltTextTitle = null,
    string? AltTextDescription = null,
    int CompactRowLabelIndent = 1,
    bool ShowExpandCollapseButtons = true,
    bool AutofitColumnsOnUpdate = true,
    bool PreserveFormattingOnUpdate = true,
    bool ShowFieldHeaders = true,
    bool ShowContextualTooltips = true,
    bool ShowPropertiesInTooltips = true,
    bool ShowClassicLayout = false,
    bool MergeAndCenterLabels = false,
    bool ShowItemsWithNoDataOnRows = false,
    bool ShowItemsWithNoDataOnColumns = false,
    bool PageOverThenDown = false,
    int PageWrap = 0,
    string? ErrorValueText = null,
    bool EnableDrill = true);

public sealed record PivotDesignOptionsValues(
    bool ShowRowGrandTotals,
    bool ShowColumnGrandTotals,
    bool ShowSubtotals,
    PivotSubtotalPlacement SubtotalPlacement,
    bool RepeatItemLabels,
    bool BlankLineAfterItems,
    string StyleName,
    PivotReportLayout ReportLayout,
    bool ShowRowHeaders,
    bool ShowColumnHeaders,
    bool ShowRowStripes,
    bool ShowColumnStripes,
    bool ShowFieldHeaders);

/// <summary>
/// Portable, UI-free planning for the PivotTable Options dialog: the report-layout and subtotal-placement
/// display catalogs (English labels), capturing the current option values off a <see cref="PivotTableModel"/>,
/// validating the compact-row-label indent box, and applying the dialog's collected values back onto a
/// values record. Single-sourced here so every desktop host shares identical behavior; the pivot application
/// session turns these values into commands while each shell retains only native dialog and rendering work.
/// </summary>
public static class PivotOptionsPlanner
{
    public const int MinCompactRowLabelIndent = 0;
    public const int MaxCompactRowLabelIndent = 15;
    public const int MinPageWrap = 0;
    public const int MaxPageWrap = 255;
    public const int MaxMissingItemsLimit = 1_048_576;
    public const int DialogWidth = 520;
    public const int DialogMinHeight = 500;
    public const int LayoutAndFormatCaptureHeight = 676;
    public const int LayoutAndFormatAvaloniaSpacerHeight = 57;

    public const string CompactIndentRangeMessage =
        "Enter a compact-form row-label indent between 0 and 15.";

    public const string PageWrapRangeMessage =
        "Enter the number of report filter fields per column between 0 and 255.";

    /// <summary>Report layouts in display order, with the English label the dialog shows.</summary>
    public static readonly IReadOnlyList<(string Label, PivotReportLayout Value)> ReportLayouts =
    [
        ("Compact", PivotReportLayout.Compact),
        ("Outline", PivotReportLayout.Outline),
        ("Tabular", PivotReportLayout.Tabular),
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

    public static string GetReportLayoutLabel(PivotReportLayout layout)
    {
        foreach (var option in ReportLayouts)
        {
            if (option.Value == layout)
                return option.Label;
        }

        return ReportLayouts[^1].Label;
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

    public static PivotDesignOptionsValues CaptureDesignValues(PivotTableModel pivotTable)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        return new PivotDesignOptionsValues(
            pivotTable.ShowRowGrandTotals,
            pivotTable.ShowColumnGrandTotals,
            pivotTable.ShowSubtotals,
            pivotTable.SubtotalPlacement,
            pivotTable.RepeatItemLabels,
            pivotTable.BlankLineAfterItems,
            pivotTable.StyleName,
            pivotTable.ReportLayout,
            pivotTable.ShowRowHeaders,
            pivotTable.ShowColumnHeaders,
            pivotTable.ShowRowStripes,
            pivotTable.ShowColumnStripes,
            pivotTable.ShowFieldHeaders);
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

    /// <summary>Validates the report-filter fields-per-column box; parses it on success.</summary>
    public static bool TryParsePageWrap(string? text, out int pageWrap, out string? error)
    {
        error = null;
        if (int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out pageWrap) &&
            pageWrap is >= MinPageWrap and <= MaxPageWrap)
        {
            return true;
        }

        pageWrap = MinPageWrap;
        error = PageWrapRangeMessage;
        return false;
    }

    /// <summary>The report-filter fields-per-column box text for the current value.</summary>
    public static string PageWrapText(int pageWrap) =>
        pageWrap.ToString(CultureInfo.CurrentCulture);

    public static int NormalizeCompactRowLabelIndent(int indent) =>
        Math.Clamp(indent, MinCompactRowLabelIndent, MaxCompactRowLabelIndent);

    public static int NormalizePageWrap(int pageWrap) =>
        Math.Clamp(pageWrap, MinPageWrap, MaxPageWrap);

    public static int? NormalizeMissingItemsLimit(int? value) =>
        value switch
        {
            null => null,
            <= 0 => 0,
            _ => MaxMissingItemsLimit
        };

    public static string? NormalizeOptionalText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return text.Trim();
    }

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
            NormalizeCompactRowLabelIndent(compactRowLabelIndent),
            repeatItemLabels,
            blankLineAfterItems,
            mergeAndCenterLabels);

    /// <summary>Snapshots the current full dialog option values off a pivot and its optional connected cache.</summary>
    public static PivotOptionsDialogValues CaptureDialogValues(PivotTableModel pivotTable, PivotCacheModel? cache = null)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);

        return CreateDialogValues(
            pivotTable.ShowRowGrandTotals,
            pivotTable.ShowColumnGrandTotals,
            pivotTable.ShowSubtotals,
            pivotTable.SubtotalPlacement,
            pivotTable.RepeatItemLabels,
            pivotTable.BlankLineAfterItems,
            pivotTable.StyleName,
            pivotTable.ShowRowHeaders,
            pivotTable.ShowColumnHeaders,
            pivotTable.ShowRowStripes,
            pivotTable.ShowColumnStripes,
            pivotTable.ReportLayout,
            pivotTable.EmptyValueText,
            refreshOnOpen: cache?.RefreshOnLoad ?? false,
            saveSourceData: cache?.SaveData ?? true,
            enableRefresh: cache?.EnableRefresh ?? true,
            preserveSourceSortFilter: cache?.PreserveSourceSortFilter ?? true,
            missingItemsLimit: cache?.MissingItemsLimit,
            printTitles: pivotTable.PrintTitles,
            printExpandCollapseButtons: pivotTable.PrintExpandCollapseButtons,
            altTextTitle: pivotTable.AltTextTitle,
            altTextDescription: pivotTable.AltTextDescription,
            compactRowLabelIndent: pivotTable.CompactRowLabelIndent,
            showExpandCollapseButtons: pivotTable.ShowExpandCollapseButtons,
            autofitColumnsOnUpdate: pivotTable.AutofitColumnsOnUpdate,
            preserveFormattingOnUpdate: pivotTable.PreserveFormattingOnUpdate,
            showFieldHeaders: pivotTable.ShowFieldHeaders,
            showContextualTooltips: pivotTable.ShowContextualTooltips,
            showPropertiesInTooltips: pivotTable.ShowPropertiesInTooltips,
            showClassicLayout: pivotTable.ShowClassicLayout,
            mergeAndCenterLabels: pivotTable.MergeAndCenterLabels,
            showItemsWithNoDataOnRows: pivotTable.ShowItemsWithNoDataOnRows,
            showItemsWithNoDataOnColumns: pivotTable.ShowItemsWithNoDataOnColumns,
            pageOverThenDown: pivotTable.PageOverThenDown,
            pageWrap: pivotTable.PageWrap,
            errorValueText: pivotTable.ErrorCaption,
            enableDrill: pivotTable.EnableDrill);
    }

    /// <summary>Builds the normalized full dialog values from collected input.</summary>
    public static PivotOptionsDialogValues CreateDialogValues(
        bool showRowGrandTotals,
        bool showColumnGrandTotals,
        bool showSubtotals,
        PivotSubtotalPlacement subtotalPlacement,
        bool repeatItemLabels,
        bool blankLineAfterItems,
        string? styleName,
        bool showRowHeaders,
        bool showColumnHeaders,
        bool showRowStripes,
        bool showColumnStripes,
        PivotReportLayout reportLayout,
        string? emptyValueText = null,
        bool refreshOnOpen = false,
        bool saveSourceData = true,
        bool enableRefresh = true,
        bool preserveSourceSortFilter = true,
        int? missingItemsLimit = null,
        bool printTitles = false,
        bool printExpandCollapseButtons = false,
        string? altTextTitle = null,
        string? altTextDescription = null,
        int compactRowLabelIndent = 1,
        bool showExpandCollapseButtons = true,
        bool autofitColumnsOnUpdate = true,
        bool preserveFormattingOnUpdate = true,
        bool showFieldHeaders = true,
        bool showContextualTooltips = true,
        bool showPropertiesInTooltips = true,
        bool showClassicLayout = false,
        bool mergeAndCenterLabels = false,
        bool showItemsWithNoDataOnRows = false,
        bool showItemsWithNoDataOnColumns = false,
        bool pageOverThenDown = false,
        int pageWrap = 0,
        string? errorValueText = null,
        bool enableDrill = true) =>
        new(
            showRowGrandTotals,
            showColumnGrandTotals,
            showSubtotals,
            subtotalPlacement,
            repeatItemLabels,
            blankLineAfterItems,
            PivotStyleGalleryPlanner.NormalizeStyleName(styleName),
            showRowHeaders,
            showColumnHeaders,
            showRowStripes,
            showColumnStripes,
            reportLayout,
            NormalizeOptionalText(emptyValueText),
            refreshOnOpen,
            saveSourceData,
            enableRefresh,
            preserveSourceSortFilter,
            NormalizeMissingItemsLimit(missingItemsLimit),
            printTitles,
            printExpandCollapseButtons,
            NormalizeOptionalText(altTextTitle),
            NormalizeOptionalText(altTextDescription),
            NormalizeCompactRowLabelIndent(compactRowLabelIndent),
            showExpandCollapseButtons,
            autofitColumnsOnUpdate,
            preserveFormattingOnUpdate,
            showFieldHeaders,
            showContextualTooltips,
            showPropertiesInTooltips,
            showClassicLayout,
            mergeAndCenterLabels,
            showItemsWithNoDataOnRows,
            showItemsWithNoDataOnColumns,
            pageOverThenDown,
            NormalizePageWrap(pageWrap),
            NormalizeOptionalText(errorValueText),
            enableDrill);
}
