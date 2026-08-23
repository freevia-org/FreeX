using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ConfigurePivotTableOptionsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly bool _showRowGrandTotals;
    private readonly bool _showColumnGrandTotals;
    private readonly bool _showSubtotals;
    private readonly PivotSubtotalPlacement _subtotalPlacement;
    private readonly bool _repeatItemLabels;
    private readonly bool _blankLineAfterItems;
    private readonly string _styleName;
    private readonly PivotReportLayout _reportLayout;
    private readonly int? _compactRowLabelIndent;
    private readonly bool _showRowHeaders;
    private readonly bool _showColumnHeaders;
    private readonly bool _showRowStripes;
    private readonly bool _showColumnStripes;
    private readonly bool? _showFieldHeaders;
    private readonly bool? _showContextualTooltips;
    private readonly bool? _showPropertiesInTooltips;
    private readonly bool? _showClassicLayout;
    private readonly bool? _mergeAndCenterLabels;
    private readonly bool? _showItemsWithNoDataOnRows;
    private readonly bool? _showItemsWithNoDataOnColumns;
    private readonly bool? _pageOverThenDown;
    private readonly int? _pageWrap;
    private readonly string? _emptyValueText;
    private readonly bool _updateEmptyValueText;
    private readonly string? _errorCaption;
    private readonly bool _updateErrorCaption;
    private readonly bool? _enableDrill;
    private readonly bool? _refreshOnOpen;
    private readonly bool? _saveSourceData;
    private readonly bool? _enableRefresh;
    private readonly bool? _preserveSourceSortFilter;
    private readonly int? _missingItemsLimit;
    private readonly bool _updateMissingItemsLimit;
    private readonly bool? _printTitles;
    private readonly bool? _printExpandCollapseButtons;
    private readonly bool? _showExpandCollapseButtons;
    private readonly bool? _autofitColumnsOnUpdate;
    private readonly bool? _preserveFormattingOnUpdate;
    private readonly string? _altTextTitle;
    private readonly string? _altTextDescription;
    private readonly bool _updateAltText;
    private PivotOptionsSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;
    private GridRange? _autofitAppliedRange;
    private Dictionary<uint, double>? _autofitColumnWidthsSnapshot;

    public ConfigurePivotTableOptionsCommand(
        SheetId sheetId,
        string pivotTableName,
        bool showRowGrandTotals,
        bool showColumnGrandTotals,
        bool showSubtotals,
        PivotSubtotalPlacement subtotalPlacement,
        bool repeatItemLabels,
        bool blankLineAfterItems,
        string styleName,
        bool showRowHeaders = true,
        bool showColumnHeaders = true,
        bool showRowStripes = false,
        bool showColumnStripes = false,
        PivotReportLayout reportLayout = PivotReportLayout.Tabular,
        string? emptyValueText = null,
        bool updateEmptyValueText = false,
        bool? refreshOnOpen = null,
        bool? saveSourceData = null,
        bool? enableRefresh = null,
        bool? preserveSourceSortFilter = null,
        int? missingItemsLimit = null,
        bool updateMissingItemsLimit = false,
        bool? printTitles = null,
        bool? printExpandCollapseButtons = null,
        string? altTextTitle = null,
        string? altTextDescription = null,
        int? compactRowLabelIndent = null,
        bool updateAltText = false,
        bool? showExpandCollapseButtons = null,
        bool? autofitColumnsOnUpdate = null,
        bool? preserveFormattingOnUpdate = null,
        bool? showFieldHeaders = null,
        bool? showContextualTooltips = null,
        bool? showPropertiesInTooltips = null,
        bool? showClassicLayout = null,
        bool? mergeAndCenterLabels = null,
        bool? showItemsWithNoDataOnRows = null,
        bool? showItemsWithNoDataOnColumns = null,
        bool? pageOverThenDown = null,
        int? pageWrap = null,
        string? errorCaption = null,
        bool updateErrorCaption = false,
        bool? enableDrill = null)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _showRowGrandTotals = showRowGrandTotals;
        _showColumnGrandTotals = showColumnGrandTotals;
        _showSubtotals = showSubtotals;
        _subtotalPlacement = subtotalPlacement;
        _repeatItemLabels = repeatItemLabels;
        _blankLineAfterItems = blankLineAfterItems;
        _styleName = styleName;
        _reportLayout = reportLayout;
        _compactRowLabelIndent = compactRowLabelIndent is { } indent
            ? NormalizeCompactRowLabelIndent(indent)
            : null;
        _showRowHeaders = showRowHeaders;
        _showColumnHeaders = showColumnHeaders;
        _showRowStripes = showRowStripes;
        _showColumnStripes = showColumnStripes;
        _showFieldHeaders = showFieldHeaders;
        _showContextualTooltips = showContextualTooltips;
        _showPropertiesInTooltips = showPropertiesInTooltips;
        _showClassicLayout = showClassicLayout;
        _mergeAndCenterLabels = mergeAndCenterLabels;
        _showItemsWithNoDataOnRows = showItemsWithNoDataOnRows;
        _showItemsWithNoDataOnColumns = showItemsWithNoDataOnColumns;
        _pageOverThenDown = pageOverThenDown;
        _pageWrap = pageWrap is { } wrap ? NormalizePageWrap(wrap) : null;
        _emptyValueText = NormalizeEmptyValueText(emptyValueText);
        _updateEmptyValueText = updateEmptyValueText;
        _errorCaption = NormalizeOptionalText(errorCaption);
        _updateErrorCaption = updateErrorCaption;
        _enableDrill = enableDrill;
        _refreshOnOpen = refreshOnOpen;
        _saveSourceData = saveSourceData;
        _enableRefresh = enableRefresh;
        _preserveSourceSortFilter = preserveSourceSortFilter;
        _missingItemsLimit = NormalizeMissingItemsLimit(missingItemsLimit);
        _updateMissingItemsLimit = updateMissingItemsLimit;
        _printTitles = printTitles;
        _printExpandCollapseButtons = printExpandCollapseButtons;
        _showExpandCollapseButtons = showExpandCollapseButtons;
        _autofitColumnsOnUpdate = autofitColumnsOnUpdate;
        _preserveFormattingOnUpdate = preserveFormattingOnUpdate;
        _altTextTitle = NormalizeEmptyValueText(altTextTitle);
        _altTextDescription = NormalizeEmptyValueText(altTextDescription);
        _updateAltText = updateAltText || _altTextTitle is not null || _altTextDescription is not null;
    }

    public string Label => "Configure PivotTable Options";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
            return CommandGuards.RejectPivotTableNotFound();

        var cache = CommandGuards.FindPivotCache(ctx.Workbook, pivotTable);
        _snapshot = PivotOptionsSnapshot.Capture(pivotTable, cache);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.LastRenderedRange ?? pivotTable.TargetRange);

        pivotTable.ShowRowGrandTotals = _showRowGrandTotals;
        pivotTable.ShowColumnGrandTotals = _showColumnGrandTotals;
        pivotTable.ShowSubtotals = _showSubtotals;
        pivotTable.SubtotalPlacement = _subtotalPlacement;
        pivotTable.RepeatItemLabels = _repeatItemLabels;
        pivotTable.BlankLineAfterItems = _blankLineAfterItems;
        pivotTable.StyleName = _styleName;
        pivotTable.ReportLayout = _reportLayout;
        if (_compactRowLabelIndent is { } compactRowLabelIndent)
            pivotTable.CompactRowLabelIndent = compactRowLabelIndent;
        pivotTable.ShowRowHeaders = _showRowHeaders;
        pivotTable.ShowColumnHeaders = _showColumnHeaders;
        pivotTable.ShowRowStripes = _showRowStripes;
        pivotTable.ShowColumnStripes = _showColumnStripes;
        if (_showFieldHeaders is { } showFieldHeaders)
            pivotTable.ShowFieldHeaders = showFieldHeaders;
        if (_showContextualTooltips is { } showContextualTooltips)
            pivotTable.ShowContextualTooltips = showContextualTooltips;
        if (_showPropertiesInTooltips is { } showPropertiesInTooltips)
            pivotTable.ShowPropertiesInTooltips = showPropertiesInTooltips;
        if (_showClassicLayout is { } showClassicLayout)
            pivotTable.ShowClassicLayout = showClassicLayout;
        if (_mergeAndCenterLabels is { } mergeAndCenterLabels)
            pivotTable.MergeAndCenterLabels = mergeAndCenterLabels;
        if (_showItemsWithNoDataOnRows is { } showItemsWithNoDataOnRows)
            pivotTable.ShowItemsWithNoDataOnRows = showItemsWithNoDataOnRows;
        if (_showItemsWithNoDataOnColumns is { } showItemsWithNoDataOnColumns)
            pivotTable.ShowItemsWithNoDataOnColumns = showItemsWithNoDataOnColumns;
        if (_pageOverThenDown is { } pageOverThenDown)
            pivotTable.PageOverThenDown = pageOverThenDown;
        if (_pageWrap is { } pageWrap)
            pivotTable.PageWrap = pageWrap;
        if (_updateEmptyValueText)
            pivotTable.EmptyValueText = _emptyValueText;
        if (_updateErrorCaption)
            pivotTable.ErrorCaption = _errorCaption;
        if (_enableDrill is { } enableDrill)
            pivotTable.EnableDrill = enableDrill;
        if (_printTitles is { } printTitles)
            pivotTable.PrintTitles = printTitles;
        if (_printExpandCollapseButtons is { } printExpandCollapseButtons)
            pivotTable.PrintExpandCollapseButtons = printExpandCollapseButtons;
        if (_showExpandCollapseButtons is { } showExpandCollapseButtons)
            pivotTable.ShowExpandCollapseButtons = showExpandCollapseButtons;
        if (_autofitColumnsOnUpdate is { } autofitColumnsOnUpdate)
            pivotTable.AutofitColumnsOnUpdate = autofitColumnsOnUpdate;
        if (_preserveFormattingOnUpdate is { } preserveFormattingOnUpdate)
            pivotTable.PreserveFormattingOnUpdate = preserveFormattingOnUpdate;
        if (_updateAltText)
        {
            pivotTable.AltTextTitle = _altTextTitle;
            pivotTable.AltTextDescription = _altTextDescription;
        }
        if (cache is not null)
        {
            if (_refreshOnOpen is { } refreshOnOpen)
                cache.RefreshOnLoad = refreshOnOpen;
            if (_saveSourceData is { } saveSourceData)
                cache.SaveData = saveSourceData;
            if (_enableRefresh is { } enableRefresh)
                cache.EnableRefresh = enableRefresh;
            if (_preserveSourceSortFilter is { } preserveSourceSortFilter)
                cache.PreserveSourceSortFilter = preserveSourceSortFilter;
            if (_updateMissingItemsLimit)
                cache.MissingItemsLimit = _missingItemsLimit;
        }

        // R140-remediation-pivot-refresh-growth-guard-completeness: ReportLayout/ShowSubtotals/
        // grand-total/header options can all change the pivot's row/column geometry on refresh, which
        // can grow the pivot's footprint past its previous render -- see
        // PivotTableRefreshService.GrowthGuard.cs.
        var snapshot = _snapshot;
        if (PivotTableCommandRefreshTransaction.RefreshGuarded(
                ctx.Workbook, sheet, pivotTable, () => snapshot!.Restore(pivotTable, cache)) is { } failure)
        {
            _snapshot = null;
            _targetSnapshot = null;
            return failure;
        }
        // R134-commands-pivotchart-stale-datarange: ReportLayout/ShowSubtotals/grand-total/header
        // options can all change the pivot's row/column geometry on refresh -- without this, a
        // PivotChart bound to this pivot table keeps rendering the cells the pivot occupied under the
        // OLD options, silently inconsistent with the pivot right next to it.
        // R45-roundtrip-not-consumed-sweep-4: AutofitColumnsOnUpdate round-tripped through this
        // dialog/XLSX but no refresh path ever consulted it, so toggling "Autofit column widths on
        // update" had no observable effect. This command's own trigger of PivotTableRefreshService.Refresh
        // (immediately above) is the natural point to honor it: when true (Excel's default), the
        // pivot's rendered range is autofit to its freshly-refreshed content, matching Excel's Refresh
        // behavior; when false, manually-set column widths are left untouched, as Excel does.
        if (pivotTable.AutofitColumnsOnUpdate && pivotTable.LastRenderedRange is { } renderedRange)
        {
            _autofitAppliedRange = renderedRange;
            _autofitColumnWidthsSnapshot = RangeSnapshot.Capture(sheet.ColumnWidths, renderedRange.Start.Col, renderedRange.End.Col);
            AutofitPivotColumns(sheet, renderedRange);
        }

        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable) && _snapshot is not null)
        {
            var cache = CommandGuards.FindPivotCache(ctx.Workbook, pivotTable);
            PivotTableRefreshService.ClearRenderedRange(sheet, pivotTable.LastRenderedRange);
            _snapshot.Restore(pivotTable, cache);
        }
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        if (_autofitAppliedRange is { } autofitRange && _autofitColumnWidthsSnapshot is not null)
            RangeSnapshot.Restore(sheet.ColumnWidths, autofitRange.Start.Col, autofitRange.End.Col, _autofitColumnWidthsSnapshot);
        if (pivotTable is not null)
            PivotTableRefreshService.UpdateBoundPivotCharts(ctx.Workbook, sheet, pivotTable);
        _snapshot = null;
        _targetSnapshot = null;
        _autofitAppliedRange = null;
        _autofitColumnWidthsSnapshot = null;
    }

    /// <summary>
    /// Resizes each column spanned by <paramref name="range"/> to fit its freshly-refreshed cell
    /// content, mirroring Excel's "Autofit column widths on update" behavior. Uses the same
    /// character-count based <see cref="AutoFitSizingService"/> that backs the Home ▸ Cells ▸
    /// Format ▸ AutoFit Column Width command — no true glyph metrics, consistent with the rest of
    /// this codebase's headless AutoFit approximation.
    /// </summary>
    private static void AutofitPivotColumns(Sheet sheet, GridRange range)
    {
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var texts = new List<string>();
            for (var row = range.Start.Row; row <= range.End.Row; row++)
            {
                if (sheet.GetCell(row, col)?.Value is { } value and not BlankValue)
                    texts.Add(FormatValueForAutofit(value));
            }

            sheet.ColumnWidths[col] = AutoFitSizingService.EstimateColumnWidth(texts, sheet.DefaultColumnWidth);
        }
    }

    private static string FormatValueForAutofit(ScalarValue value) => value switch
    {
        NumberValue number => number.Value.ToString("G15", CultureInfo.InvariantCulture),
        DateTimeValue dateTime => dateTime.Value.ToString("G15", CultureInfo.InvariantCulture),
        TextValue text => text.Value,
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => error.Code,
        _ => value.ToString() ?? ""
    };

    private sealed record PivotOptionsSnapshot(
        bool ShowRowGrandTotals,
        bool ShowColumnGrandTotals,
        bool ShowSubtotals,
        PivotSubtotalPlacement SubtotalPlacement,
        bool RepeatItemLabels,
        bool BlankLineAfterItems,
        string StyleName,
        PivotReportLayout ReportLayout,
        int CompactRowLabelIndent,
        bool ShowRowHeaders,
        bool ShowColumnHeaders,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        bool ShowFieldHeaders,
        bool ShowContextualTooltips,
        bool ShowPropertiesInTooltips,
        bool ShowClassicLayout,
        bool MergeAndCenterLabels,
        bool ShowItemsWithNoDataOnRows,
        bool ShowItemsWithNoDataOnColumns,
        bool PageOverThenDown,
        int PageWrap,
        string? EmptyValueText,
        string? ErrorCaption,
        bool? RefreshOnLoad,
        bool? SaveData,
        bool? EnableRefresh,
        bool? PreserveSourceSortFilter,
        int? MissingItemsLimit,
        bool PrintTitles,
        bool PrintExpandCollapseButtons,
        bool ShowExpandCollapseButtons,
        bool AutofitColumnsOnUpdate,
        bool PreserveFormattingOnUpdate,
        string? AltTextTitle,
        string? AltTextDescription,
        bool EnableDrill,
        GridRange? LastRenderedRange)
    {
        public static PivotOptionsSnapshot Capture(PivotTableModel pivotTable, PivotCacheModel? cache) =>
            new(
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ReportLayout,
                pivotTable.CompactRowLabelIndent,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ShowFieldHeaders,
                pivotTable.ShowContextualTooltips,
                pivotTable.ShowPropertiesInTooltips,
                pivotTable.ShowClassicLayout,
                pivotTable.MergeAndCenterLabels,
                pivotTable.ShowItemsWithNoDataOnRows,
                pivotTable.ShowItemsWithNoDataOnColumns,
                pivotTable.PageOverThenDown,
                pivotTable.PageWrap,
                pivotTable.EmptyValueText,
                pivotTable.ErrorCaption,
                cache?.RefreshOnLoad,
                cache?.SaveData,
                cache?.EnableRefresh,
                cache?.PreserveSourceSortFilter,
                cache?.MissingItemsLimit,
                pivotTable.PrintTitles,
                pivotTable.PrintExpandCollapseButtons,
                pivotTable.ShowExpandCollapseButtons,
                pivotTable.AutofitColumnsOnUpdate,
                pivotTable.PreserveFormattingOnUpdate,
                pivotTable.AltTextTitle,
                pivotTable.AltTextDescription,
                pivotTable.EnableDrill,
                pivotTable.LastRenderedRange);

        public void Restore(PivotTableModel pivotTable, PivotCacheModel? cache)
        {
            pivotTable.ShowRowGrandTotals = ShowRowGrandTotals;
            pivotTable.ShowColumnGrandTotals = ShowColumnGrandTotals;
            pivotTable.ShowSubtotals = ShowSubtotals;
            pivotTable.SubtotalPlacement = SubtotalPlacement;
            pivotTable.RepeatItemLabels = RepeatItemLabels;
            pivotTable.BlankLineAfterItems = BlankLineAfterItems;
            pivotTable.StyleName = StyleName;
            pivotTable.ReportLayout = ReportLayout;
            pivotTable.CompactRowLabelIndent = CompactRowLabelIndent;
            pivotTable.ShowRowHeaders = ShowRowHeaders;
            pivotTable.ShowColumnHeaders = ShowColumnHeaders;
            pivotTable.ShowRowStripes = ShowRowStripes;
            pivotTable.ShowColumnStripes = ShowColumnStripes;
            pivotTable.ShowFieldHeaders = ShowFieldHeaders;
            pivotTable.ShowContextualTooltips = ShowContextualTooltips;
            pivotTable.ShowPropertiesInTooltips = ShowPropertiesInTooltips;
            pivotTable.ShowClassicLayout = ShowClassicLayout;
            pivotTable.MergeAndCenterLabels = MergeAndCenterLabels;
            pivotTable.ShowItemsWithNoDataOnRows = ShowItemsWithNoDataOnRows;
            pivotTable.ShowItemsWithNoDataOnColumns = ShowItemsWithNoDataOnColumns;
            pivotTable.PageOverThenDown = PageOverThenDown;
            pivotTable.PageWrap = PageWrap;
            pivotTable.EmptyValueText = EmptyValueText;
            pivotTable.ErrorCaption = ErrorCaption;
            pivotTable.PrintTitles = PrintTitles;
            pivotTable.PrintExpandCollapseButtons = PrintExpandCollapseButtons;
            pivotTable.ShowExpandCollapseButtons = ShowExpandCollapseButtons;
            pivotTable.AutofitColumnsOnUpdate = AutofitColumnsOnUpdate;
            pivotTable.PreserveFormattingOnUpdate = PreserveFormattingOnUpdate;
            pivotTable.AltTextTitle = AltTextTitle;
            pivotTable.AltTextDescription = AltTextDescription;
            pivotTable.EnableDrill = EnableDrill;
            pivotTable.LastRenderedRange = LastRenderedRange;
            if (cache is not null)
            {
                if (RefreshOnLoad is { } refreshOnLoad)
                    cache.RefreshOnLoad = refreshOnLoad;
                if (SaveData is { } saveData)
                    cache.SaveData = saveData;
                if (EnableRefresh is { } enableRefresh)
                    cache.EnableRefresh = enableRefresh;
                if (PreserveSourceSortFilter is { } preserveSourceSortFilter)
                    cache.PreserveSourceSortFilter = preserveSourceSortFilter;
                cache.MissingItemsLimit = MissingItemsLimit;
            }
        }
    }

    private static string? NormalizeEmptyValueText(string? text)
    {
        return NormalizeOptionalText(text);
    }

    private static string? NormalizeOptionalText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return text.Trim();
    }

    private static int NormalizeCompactRowLabelIndent(int indent) => Math.Clamp(indent, 0, 15);

    private static int NormalizePageWrap(int pageWrap) => Math.Clamp(pageWrap, 0, 255);

    private static int? NormalizeMissingItemsLimit(int? value) =>
        value switch
        {
            null => null,
            <= 0 => 0,
            _ => 1_048_576
        };
}

