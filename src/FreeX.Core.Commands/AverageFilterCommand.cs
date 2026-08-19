using FreeX.Core.Model;
using System.Buffers;

namespace FreeX.Core.Commands;

public sealed class AverageFilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;
    private readonly bool _above;
    private FilterUndoSnapshot _undoSnapshot;
    // R33-commands-autofilter-slicer-1: keep the worksheet AutoFilter's <dynamicFilter> filterColumn
    // model in sync with the interactively-applied Above/Below Average criterion, so it round-trips
    // through XlsxWorksheetAutoFilterXmlMapper instead of being silently dropped on save.
    //
    // R106-commands-autofilter-table-sync-1: WorksheetAutoFilterColumnSync above is a no-op whenever
    // _range is a structured table's own Range -- unlike the sibling TopBottomFilterCommand (<top10>)
    // and FilterConditionCommand (<customFilters>), this command intentionally does NOT mirror its
    // criterion into the table's own FilterColumns model yet. A raw <dynamicFilter> passthrough into
    // a table's <autoFilter> (the same NativeFilterXmls approach TopBottomFilterCommand uses for
    // <top10>) was attempted and reverted: it crashes
    // ClosedXML.Excel.XLWorkbook.LoadAutoFilterColumns with a NullReferenceException the moment
    // FreeX's own real Load path (XlsxFileAdapter -> OpenClosedXmlWorkbookWithSanitizationFallback)
    // re-opens the saved file -- verified via a failing round-trip test. FreeX already has to work
    // around the identical ClosedXML limitation for WORKSHEET-level <dynamicFilter> elements
    // (XlsxClosedXmlLoadPackageSanitizer.HasWorksheetDynamicFilters/RemoveWorksheetDynamicFilters
    // strips them from xl/worksheets/*.xml before handing the package to ClosedXML), but that
    // scan+strip does not yet extend to xl/tables/*.xml parts. Until that Core.IO gap is closed,
    // writing a table-level <dynamicFilter> would trade a silent-drop bug for a load crash, which is
    // strictly worse -- so this command is left writing ONLY the (harmless, already-covered)
    // worksheet-level AutoFilter model for now. See the R106 fix round's siblingLeads.
    private List<WorksheetAutoFilterColumnModel>? _previousAutoFilterColumns;

    public string Label => _above ? "Above Average Filter" : "Below Average Filter";

    public AverageFilterCommand(SheetId sheetId, GridRange range, uint filterColOffset, bool above)
    {
        _sheetId = sheetId;
        _range = range;
        _filterColOffset = filterColOffset;
        _above = above;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, _filterColOffset) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _undoSnapshot.Reset();

        _previousAutoFilterColumns = WorksheetAutoFilterColumnSync.Apply(
            sheet,
            _range,
            (int)_filterColOffset,
            new WorksheetAutoFilterColumnModel(
                ColumnId: (int)_filterColOffset,
                Values: [],
                IncludeBlank: false,
                CustomFilters: [],
                CustomFiltersAnd: false,
                CustomFiltersAndRaw: null,
                NativeCustomFiltersAttributes: null,
                Top10: null,
                DynamicFilter: new WorksheetAutoFilterDynamicFilterModel(Type: _above ? "aboveAverage" : "belowAverage"),
                ColorFilter: null,
                IconFilter: null,
                DateGroups: [],
                NativeFiltersAttributes: null,
                NativeFilterXmls: []));

        var filterCol = _range.Start.Col + _filterColOffset;
        // table-semantics-F1: see FilterHiddenRowUpdater.GetFilterableFirstRow -- a headerless
        // table's first row is itself a data row and must participate in the Above/Below-Average
        // statistic and hiding.
        var firstDataRow = FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, _range);
        // R100-commands-filter-totalsrow-1: see FilterCommand.RecomputeHiddenRows -- exclude a
        // structured table's shown Totals Row from the Above/Below-Average data set and statistic.
        var lastDataRow = StructuredTableEditEffects.GetFilterableLastRow(sheet, _range);
        if (firstDataRow > lastDataRow)
            return new CommandOutcome(true);

        var dataRowCount = (int)Math.Min(lastDataRow - firstDataRow + 1, (uint)int.MaxValue);
        var values = ArrayPool<double>.Shared.Rent(dataRowCount);
        var numericCount = 0;
        var sum = 0d;

        try
        {
            for (var offset = 0; offset < dataRowCount; offset++)
            {
                var row = firstDataRow + (uint)offset;

                // R56-services-autofilter-sort-5-1: a row already hidden by another column's active
                // filter is excluded from Excel's Above/Below-Average statistic (and, via the NaN
                // sentinel below, from the resulting visibility decision for that row too).
                if (FilterHiddenRowUpdater.IsHiddenByAnyOtherActiveMechanism(sheet, filterCol, row))
                {
                    values[offset] = double.NaN;
                    continue;
                }

                if (sheet.GetValue(row, filterCol) is NumberValue number)
                {
                    values[offset] = number.Value;
                    numericCount++;
                    sum += number.Value;
                }
                else
                {
                    values[offset] = double.NaN;
                }
            }

            if (numericCount == 0)
            {
                if (!sheet.ColumnFilterOwnedRows.TryGetValue(filterCol, out var ownedRows) || ownedRows.Count == 0)
                    return new CommandOutcome(true);

                _undoSnapshot.CaptureIfNeeded(sheet);
                FilterHiddenRowUpdater.ClearColumnOwnedRange(sheet, filterCol, _range);
                return new CommandOutcome(true);
            }

            var average = sum / numericCount;

            for (var offset = 0; offset < dataRowCount; offset++)
            {
                var value = values[offset];
                var visible = !double.IsNaN(value) && (_above ? value > average : value < average);
                var row = firstDataRow + (uint)offset;
                if (FilterHiddenRowUpdater.IsColumnOwnedVisibilityAlreadyCorrect(sheet, filterCol, row, visible))
                    continue;

                _undoSnapshot.CaptureIfNeeded(sheet);
                FilterHiddenRowUpdater.ApplyColumnOwnedVisibility(sheet, filterCol, row, visible);
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(values);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        WorksheetAutoFilterColumnSync.Restore(sheet, _range, _previousAutoFilterColumns);

        if (!_undoSnapshot.HasSnapshot)
            return;

        _undoSnapshot.Restore(sheet);
    }
}
