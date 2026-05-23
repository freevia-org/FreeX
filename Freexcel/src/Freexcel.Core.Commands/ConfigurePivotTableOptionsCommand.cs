using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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
    private readonly bool? _pageOverThenDown;
    private readonly int? _pageWrap;
    private readonly string? _emptyValueText;
    private readonly bool _updateEmptyValueText;
    private readonly bool? _refreshOnOpen;
    private readonly bool? _saveSourceData;
    private readonly bool? _enableRefresh;
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
        bool? pageOverThenDown = null,
        int? pageWrap = null)
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
        _pageOverThenDown = pageOverThenDown;
        _pageWrap = pageWrap is { } wrap ? NormalizePageWrap(wrap) : null;
        _emptyValueText = NormalizeEmptyValueText(emptyValueText);
        _updateEmptyValueText = updateEmptyValueText;
        _refreshOnOpen = refreshOnOpen;
        _saveSourceData = saveSourceData;
        _enableRefresh = enableRefresh;
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
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

        var cache = ctx.Workbook.PivotCaches.FirstOrDefault(item => item.CacheId == pivotTable.CacheId);
        _snapshot = PivotOptionsSnapshot.Capture(pivotTable, cache);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);

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
        if (_pageOverThenDown is { } pageOverThenDown)
            pivotTable.PageOverThenDown = pageOverThenDown;
        if (_pageWrap is { } pageWrap)
            pivotTable.PageWrap = pageWrap;
        if (_updateEmptyValueText)
            pivotTable.EmptyValueText = _emptyValueText;
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
            if (_updateMissingItemsLimit)
                cache.MissingItemsLimit = _missingItemsLimit;
        }

        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        var cache = pivotTable is null
            ? null
            : ctx.Workbook.PivotCaches.FirstOrDefault(item => item.CacheId == pivotTable.CacheId);
        if (pivotTable is not null && _snapshot is not null)
            _snapshot.Restore(pivotTable, cache);
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        _snapshot = null;
        _targetSnapshot = null;
    }

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
        bool PageOverThenDown,
        int PageWrap,
        string? EmptyValueText,
        bool? RefreshOnLoad,
        bool? SaveData,
        bool? EnableRefresh,
        int? MissingItemsLimit,
        bool PrintTitles,
        bool PrintExpandCollapseButtons,
        bool ShowExpandCollapseButtons,
        bool AutofitColumnsOnUpdate,
        bool PreserveFormattingOnUpdate,
        string? AltTextTitle,
        string? AltTextDescription)
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
                pivotTable.PageOverThenDown,
                pivotTable.PageWrap,
                pivotTable.EmptyValueText,
                cache?.RefreshOnLoad,
                cache?.SaveData,
                cache?.EnableRefresh,
                cache?.MissingItemsLimit,
                pivotTable.PrintTitles,
                pivotTable.PrintExpandCollapseButtons,
                pivotTable.ShowExpandCollapseButtons,
                pivotTable.AutofitColumnsOnUpdate,
                pivotTable.PreserveFormattingOnUpdate,
                pivotTable.AltTextTitle,
                pivotTable.AltTextDescription);

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
            pivotTable.PageOverThenDown = PageOverThenDown;
            pivotTable.PageWrap = PageWrap;
            pivotTable.EmptyValueText = EmptyValueText;
            pivotTable.PrintTitles = PrintTitles;
            pivotTable.PrintExpandCollapseButtons = PrintExpandCollapseButtons;
            pivotTable.ShowExpandCollapseButtons = ShowExpandCollapseButtons;
            pivotTable.AutofitColumnsOnUpdate = AutofitColumnsOnUpdate;
            pivotTable.PreserveFormattingOnUpdate = PreserveFormattingOnUpdate;
            pivotTable.AltTextTitle = AltTextTitle;
            pivotTable.AltTextDescription = AltTextDescription;
            if (cache is not null)
            {
                if (RefreshOnLoad is { } refreshOnLoad)
                    cache.RefreshOnLoad = refreshOnLoad;
                if (SaveData is { } saveData)
                    cache.SaveData = saveData;
                if (EnableRefresh is { } enableRefresh)
                    cache.EnableRefresh = enableRefresh;
                cache.MissingItemsLimit = MissingItemsLimit;
            }
        }
    }

    private static string? NormalizeEmptyValueText(string? text)
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

