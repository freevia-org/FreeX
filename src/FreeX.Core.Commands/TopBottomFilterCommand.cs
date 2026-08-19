using FreeX.Core.Model;
using System.Buffers;
using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.Commands;

public sealed class TopBottomFilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;
    private readonly uint _count;
    private readonly bool _top;
    private readonly bool _percent;
    private FilterUndoSnapshot _undoSnapshot;
    // R33-commands-autofilter-slicer-1: keep the worksheet AutoFilter's <top10> filterColumn model in
    // sync with the interactively-applied Top 10/Bottom 10 (items or percent) criterion, so it
    // round-trips through XlsxWorksheetAutoFilterXmlMapper instead of being silently dropped on save.
    private List<WorksheetAutoFilterColumnModel>? _previousAutoFilterColumns;
    // R106-commands-autofilter-table-sync-1: WorksheetAutoFilterColumnSync above is a no-op whenever
    // _range is a structured table's own Range (tables carry their own <autoFilter> rather than a
    // worksheet-level one) -- keep the TABLE's own FilterColumns model in sync too (mirrors
    // FilterCommand.ApplyToStructuredTableIfMatched for the value-list case, finding H18), otherwise
    // a Top 10/Bottom N filter applied from a Table's header dropdown hides/shows rows live but is
    // silently dropped from the table's <autoFilter> XML on save/reload.
    private StructuredTableFilterColumnSnapshot? _tableFilterSnapshot;

    public string Label => (_top, _percent) switch
    {
        (true, true) => "Top Percent Filter",
        (false, true) => "Bottom Percent Filter",
        (true, false) => "Top Items Filter",
        _ => "Bottom Items Filter"
    };

    public TopBottomFilterCommand(SheetId sheetId, GridRange range, uint filterColOffset, uint count, bool top)
        : this(sheetId, range, filterColOffset, count, top, percent: false)
    {
    }

    private TopBottomFilterCommand(SheetId sheetId, GridRange range, uint filterColOffset, uint count, bool top, bool percent)
    {
        _sheetId = sheetId;
        _range = range;
        _filterColOffset = filterColOffset;
        _count = count;
        _top = top;
        _percent = percent;
    }

    public static TopBottomFilterCommand Percent(
        SheetId sheetId,
        GridRange range,
        uint filterColOffset,
        uint percent,
        bool top) =>
        new(sheetId, range, filterColOffset, percent, top, percent: true);

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, _filterColOffset) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _undoSnapshot.Reset();

        var filterCol = _range.Start.Col + _filterColOffset;

        if (_count == 0)
        {
            _previousAutoFilterColumns = WorksheetAutoFilterColumnSync.Apply(sheet, _range, (int)_filterColOffset, null);
            _tableFilterSnapshot = StructuredTableFilterColumnSync.Apply(sheet, _range, (int)_filterColOffset, null);

            if (!sheet.ColumnFilterOwnedRows.TryGetValue(filterCol, out var ownedRows) || ownedRows.Count == 0)
                return new CommandOutcome(true);

            _undoSnapshot.CaptureIfNeeded(sheet);
            FilterHiddenRowUpdater.ClearColumnOwnedRange(sheet, filterCol, _range);
            return new CommandOutcome(true);
        }

        // table-semantics-F1: see FilterHiddenRowUpdater.GetFilterableFirstRow -- a headerless
        // table's first row is itself a data row and must participate in Top-N ranking/hiding.
        var firstDataRow = FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, _range);
        // R100-commands-filter-totalsrow-1: see FilterCommand.RecomputeHiddenRows -- exclude a
        // structured table's shown Totals Row from the filterable/Top-N data set.
        var lastDataRow = StructuredTableEditEffects.GetFilterableLastRow(sheet, _range);

        // R96-commands-topbottom-filterval-1: compute the boundary value (and the kept-row mask)
        // BEFORE building the persisted Top10 criterion below, so the boundary Excel's tie-inclusive
        // Top-N semantics computed (see SelectBestRows) can be carried into the model's FilterValue
        // instead of being discarded once ApplyKeptRowVisibility runs.
        double? filterValue = null;
        bool[]? keptRows = null;
        var allNumericVisible = false;

        if (firstDataRow <= lastDataRow)
        {
            var dataRowCount = (int)Math.Min(lastDataRow - firstDataRow + 1, (uint)int.MaxValue);
            var keepCount = _percent
                ? GetPercentKeepCount(sheet, filterCol, firstDataRow, lastDataRow)
                : (int)Math.Min(_count, (uint)dataRowCount);

            if (keepCount >= dataRowCount)
            {
                allNumericVisible = true;
            }
            else
            {
                keptRows = ArrayPool<bool>.Shared.Rent(dataRowCount);
                Array.Clear(keptRows, 0, dataRowCount);

                if (keepCount > 0)
                    filterValue = SelectBestRows(sheet, filterCol, firstDataRow, lastDataRow, keepCount, _top, keptRows);
            }
        }

        // R96-commands-topbottom-filterval-1: persist the boundary value as the Top10 criterion's
        // FilterValue so it round-trips through XlsxWorksheetAutoFilterXmlMapper.ToTop10Xml's
        // <top10 filterVal=.../> attribute -- without it, XlsxWorksheetAutoFilterMaterializer.
        // BuildTop10KeptRows falls back to a naive Take(N) on reload that arbitrarily drops tied
        // rows past the Nth position that this live apply correctly kept visible.
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
                Top10: new WorksheetAutoFilterTop10Model(Top: _top, Percent: _percent, Value: _count, FilterValue: filterValue),
                DynamicFilter: null,
                ColorFilter: null,
                IconFilter: null,
                DateGroups: [],
                NativeFiltersAttributes: null,
                NativeFilterXmls: []));

        // R106-commands-autofilter-table-sync-1: mirror the same Top10 criterion into the owning
        // structured table's FilterColumns model (a no-op when _range isn't a table's own Range).
        // StructuredTableFilterColumnModel has no first-class Top10 field, so the criterion is
        // carried as the exact raw <top10> XML XlsxStructuredTableWriter/XlsxStructuredTableMetadataReader
        // already pass through verbatim for any filterColumn child they don't model directly (the same
        // mechanism that already round-trips a Top10 filter Excel itself wrote into a table).
        _tableFilterSnapshot = StructuredTableFilterColumnSync.Apply(
            sheet,
            _range,
            (int)_filterColOffset,
            new StructuredTableFilterColumnModel(
                (int)_filterColOffset,
                Values: [],
                IncludeBlank: false,
                NativeFilterXmls: [BuildTop10Xml(_top, _percent, _count, filterValue)]));

        try
        {
            if (allNumericVisible)
                ApplyNumericVisibility(sheet, filterCol, firstDataRow, lastDataRow);
            else if (keptRows is not null)
                ApplyKeptRowVisibility(sheet, filterCol, firstDataRow, lastDataRow, keptRows);
        }
        finally
        {
            if (keptRows is not null)
                ArrayPool<bool>.Shared.Return(keptRows);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        WorksheetAutoFilterColumnSync.Restore(sheet, _range, _previousAutoFilterColumns);
        StructuredTableFilterColumnSync.Restore(sheet, _tableFilterSnapshot);

        if (!_undoSnapshot.HasSnapshot)
            return;

        _undoSnapshot.Restore(sheet);
    }

    /// <summary>
    /// Serializes the Top10 criterion into the raw spreadsheetml &lt;top10&gt; XML
    /// XlsxStructuredTableWriter's NativeFilterXmls passthrough expects verbatim -- mirrors
    /// XlsxWorksheetAutoFilterXmlMapper.ToTop10Xml's own attribute-omission rules (top/percent
    /// attributes are only written when they differ from their OOXML default) so the table's
    /// &lt;top10&gt; is byte-shape-identical to what the worksheet-level AutoFilter path would emit
    /// for the same criterion.
    /// </summary>
    private static string BuildTop10Xml(bool top, bool percent, uint value, double? filterValue)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var element = new XElement(ns + "top10");
        if (!top)
            element.SetAttributeValue("top", "0");
        if (percent)
            element.SetAttributeValue("percent", "1");
        element.SetAttributeValue("val", value.ToString(CultureInfo.InvariantCulture));
        if (filterValue is not null)
            element.SetAttributeValue("filterVal", filterValue.Value.ToString(CultureInfo.InvariantCulture));

        return element.ToString(SaveOptions.DisableFormatting);
    }

    private int GetPercentKeepCount(Sheet sheet, uint filterCol, uint firstDataRow, uint lastDataRow)
    {
        var numericCount = 0;
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            // R56-services-autofilter-sort-5-1: a row already hidden by another column's active
            // filter is not part of the visible dataset Excel scopes Top-N/percent against.
            if (FilterHiddenRowUpdater.IsHiddenByAnyOtherActiveMechanism(sheet, filterCol, row))
                continue;

            if (sheet.GetValue(row, filterCol) is NumberValue)
                numericCount++;
        }

        return (int)Math.Ceiling(numericCount * (Math.Min(_count, 100u) / 100.0));
    }

    /// <summary>
    /// Selects the Top-N/Bottom-N rows into <paramref name="keptRows"/> and returns the boundary
    /// (Nth-best) value used, or <c>null</c> when fewer numeric/visible rows exist than
    /// <paramref name="keepCount"/> (in which case every one of them qualifies and there is no
    /// meaningful cutoff to persist).
    /// </summary>
    private static double? SelectBestRows(
        Sheet sheet,
        uint filterCol,
        uint firstDataRow,
        uint lastDataRow,
        int keepCount,
        bool top,
        bool[] keptRows)
    {
        var heap = ArrayPool<RankedFilterRow>.Shared.Rent(keepCount);
        var heapCount = 0;

        try
        {
            for (var row = firstDataRow; row <= lastDataRow; row++)
            {
                // R56-services-autofilter-sort-5-1: skip rows already hidden by another column's
                // active filter -- Excel computes the Top-N boundary only over the visible dataset.
                if (FilterHiddenRowUpdater.IsHiddenByAnyOtherActiveMechanism(sheet, filterCol, row))
                    continue;
                if (sheet.GetValue(row, filterCol) is not NumberValue number)
                    continue;

                var candidate = new RankedFilterRow(row, number.Value);
                if (heapCount < keepCount)
                {
                    heap[heapCount] = candidate;
                    SiftUpWorstFirst(heap, heapCount, top);
                    heapCount++;
                }
                else if (IsBetter(candidate, heap[0], top))
                {
                    heap[0] = candidate;
                    SiftDownWorstFirst(heap, heapCount, top);
                }
            }

            if (heapCount < keepCount)
            {
                // Fewer numeric (visible) rows than requested count: everything visible+numeric qualifies.
                for (var row = firstDataRow; row <= lastDataRow; row++)
                {
                    if (FilterHiddenRowUpdater.IsHiddenByAnyOtherActiveMechanism(sheet, filterCol, row))
                        continue;
                    if (sheet.GetValue(row, filterCol) is NumberValue)
                        keptRows[(int)(row - firstDataRow)] = true;
                }

                return null;
            }
            else
            {
                // Excel Top-N/Bottom-N is threshold-based: keep every row at least as good as
                // the Nth-best (boundary) value, not just the first N by row index, so ties at
                // the boundary are all kept (e.g. Top 2 over {100,100,100,50} keeps all three 100s).
                var boundary = heap[0].Value;
                for (var row = firstDataRow; row <= lastDataRow; row++)
                {
                    if (FilterHiddenRowUpdater.IsHiddenByAnyOtherActiveMechanism(sheet, filterCol, row))
                        continue;
                    if (sheet.GetValue(row, filterCol) is not NumberValue number)
                        continue;

                    var keep = top ? number.Value >= boundary : number.Value <= boundary;
                    if (keep)
                        keptRows[(int)(row - firstDataRow)] = true;
                }

                return boundary;
            }
        }
        finally
        {
            ArrayPool<RankedFilterRow>.Shared.Return(heap);
        }
    }

    private void ApplyNumericVisibility(
        Sheet sheet,
        uint filterCol,
        uint firstDataRow,
        uint lastDataRow)
    {
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            var visible = sheet.GetValue(row, filterCol) is NumberValue;
            if (FilterHiddenRowUpdater.IsColumnOwnedVisibilityAlreadyCorrect(sheet, filterCol, row, visible))
                continue;

            _undoSnapshot.CaptureIfNeeded(sheet);
            FilterHiddenRowUpdater.ApplyColumnOwnedVisibility(sheet, filterCol, row, visible);
        }
    }

    private void ApplyKeptRowVisibility(
        Sheet sheet,
        uint filterCol,
        uint firstDataRow,
        uint lastDataRow,
        bool[] keptRows)
    {
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            var visible = keptRows[(int)(row - firstDataRow)];
            if (FilterHiddenRowUpdater.IsColumnOwnedVisibilityAlreadyCorrect(sheet, filterCol, row, visible))
                continue;

            _undoSnapshot.CaptureIfNeeded(sheet);
            FilterHiddenRowUpdater.ApplyColumnOwnedVisibility(sheet, filterCol, row, visible);
        }
    }

    private readonly record struct RankedFilterRow(uint Row, double Value);

    private static void SiftUpWorstFirst(RankedFilterRow[] heap, int index, bool top)
    {
        while (index > 0)
        {
            var parent = (index - 1) / 2;
            if (!IsWorse(heap[index], heap[parent], top))
                return;

            (heap[parent], heap[index]) = (heap[index], heap[parent]);
            index = parent;
        }
    }

    private static void SiftDownWorstFirst(RankedFilterRow[] heap, int count, bool top)
    {
        var index = 0;
        while (true)
        {
            var left = index * 2 + 1;
            if (left >= count)
                return;

            var worstChild = left;
            var right = left + 1;
            if (right < count && IsWorse(heap[right], heap[left], top))
                worstChild = right;

            if (!IsWorse(heap[worstChild], heap[index], top))
                return;

            (heap[index], heap[worstChild]) = (heap[worstChild], heap[index]);
            index = worstChild;
        }
    }

    private static bool IsBetter(RankedFilterRow candidate, RankedFilterRow currentWorst, bool top)
    {
        if (candidate.Value != currentWorst.Value)
            return top ? candidate.Value > currentWorst.Value : candidate.Value < currentWorst.Value;

        return candidate.Row < currentWorst.Row;
    }

    private static bool IsWorse(RankedFilterRow candidate, RankedFilterRow other, bool top)
    {
        if (candidate.Value != other.Value)
            return top ? candidate.Value < other.Value : candidate.Value > other.Value;

        return candidate.Row > other.Row;
    }

}
