using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

// ---------------------------------------------------------------------------
// Shared data types
// ---------------------------------------------------------------------------

/// <summary>
/// Identifies a pivot header cell that should carry a dropdown arrow button, along with whether
/// the field currently has an active sort/filter/selection (so the renderer can badge it).
/// Also carries the portable target model for menu construction so callers do not need to look
/// up the pivot table or field a second time.
/// </summary>
public readonly record struct PivotHeaderDropdownTarget(
    CellAddress HeaderCell,
    bool IsActive,
    PivotHeaderDropdownTargetModel MenuTarget);

/// <summary>
/// Describes a row-label cell that needs expand/collapse adornment rendering or extra text padding.
/// Lives in shared presentation code so that all renderers can consume it without taking a
/// UI-framework dependency.
/// </summary>
public readonly record struct PivotRowLabelAdornment(
    CellAddress Cell,
    int IndentLevel,
    bool ShowExpandCollapseButton,
    bool IsExpanded,
    bool ReserveTextPadding = true);

// ---------------------------------------------------------------------------
// Expand/collapse state
// ---------------------------------------------------------------------------

/// <summary>
/// Portable, in-memory expand/collapse state for pivot row/column field items -- R92-app-pivot-drilldown-5-1.
/// Collapse applies at the (pivot table, source field, item value) level, matching Excel/OOXML's own
/// item "e" (expanded) attribute semantics: collapsing an item value (e.g. "East") hides every
/// occurrence of that value's descendant rows across the whole field, not just one specific parent
/// occurrence. A shell host owns one instance per open workbook/session and passes it to
/// <see cref="PivotGridAdornmentPlanner.BuildRowLabelAdornments"/> so the drawn +/- glyph
/// (<see cref="PivotRowLabelAdornment.IsExpanded"/>) reflects the user's actual collapse choice
/// instead of always claiming "expanded".
///
/// SCOPE (R92-app-pivot-drilldown-5-1 is a partial fix -- see round notes): this state is
/// session-only. It is not yet:
/// <list type="bullet">
/// <item>parsed from an opened .xlsx's <c>&lt;item e="0"/&gt;</c> attribute (<c>XlsxPivotTableReader</c>),</item>
/// <item>written back out on save (<c>XlsxPivotTableWriter</c>), or</item>
/// <item>wired to a click handler (<c>GridView.Input.cs</c>/<c>GridView.HitTesting.cs</c> have no hit-test
/// for <see cref="PivotRowLabelAdornment.ShowExpandCollapseButton"/> yet).</item>
/// </list>
/// Nor does collapsing an item hide its descendant rows at pivot-refresh time (<c>PivotTableRefreshService</c>) --
/// this state only changes what the adornment glyph/planner reports, matching this fix's scope: the
/// adornment/state model in the portable planner, not the full round-trip or interactive drill-down.
/// </summary>
public sealed class PivotCollapseState
{
    private readonly Dictionary<string, HashSet<string>> _collapsedItemsByField =
        new(StringComparer.Ordinal);

    /// <summary>True when <paramref name="itemValue"/> is currently collapsed for this field.</summary>
    public bool IsCollapsed(string pivotTableName, int sourceFieldIndex, string itemValue) =>
        _collapsedItemsByField.TryGetValue(FieldKey(pivotTableName, sourceFieldIndex), out var items) &&
        items.Contains(itemValue);

    /// <summary>Sets or clears the collapsed flag for a specific item value of a specific field.</summary>
    public void SetCollapsed(string pivotTableName, int sourceFieldIndex, string itemValue, bool collapsed)
    {
        var key = FieldKey(pivotTableName, sourceFieldIndex);
        if (collapsed)
        {
            if (!_collapsedItemsByField.TryGetValue(key, out var items))
            {
                items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _collapsedItemsByField[key] = items;
            }
            items.Add(itemValue);
        }
        else if (_collapsedItemsByField.TryGetValue(key, out var items))
        {
            items.Remove(itemValue);
        }
    }

    /// <summary>Flips the collapsed flag for a specific item value of a specific field.</summary>
    public void ToggleCollapsed(string pivotTableName, int sourceFieldIndex, string itemValue) =>
        SetCollapsed(
            pivotTableName, sourceFieldIndex, itemValue,
            !IsCollapsed(pivotTableName, sourceFieldIndex, itemValue));

    private static string FieldKey(string pivotTableName, int sourceFieldIndex) =>
        pivotTableName + "|" + sourceFieldIndex.ToString(CultureInfo.InvariantCulture);
}

// ---------------------------------------------------------------------------
// Planning entry point
// ---------------------------------------------------------------------------

/// <summary>
/// Builds pivot grid overlay adornments — header dropdown targets and row-label expand/collapse
/// adornments — from a sheet's pivot tables. The logic is identical to the desktop-host planners
/// but the types live in shared presentation code so that all renderers can consume them without
/// taking a UI-framework dependency.
/// </summary>
public static class PivotGridAdornmentPlanner
{
    // -----------------------------------------------------------------------
    // Header dropdown targets
    // -----------------------------------------------------------------------

    public static IReadOnlyList<PivotHeaderDropdownTarget> BuildHeaderTargets(Workbook workbook, Sheet sheet)
    {
        if (sheet.PivotTables.Count == 0)
            return [];

        var targets = new List<PivotHeaderDropdownTarget>();
        foreach (var pivotTable in sheet.PivotTables)
            AddHeaderTargets(workbook, sheet, pivotTable, targets);
        return targets;
    }

    private static void AddHeaderTargets(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        List<PivotHeaderDropdownTarget> targets)
    {
        if (!pivotTable.ShowFieldHeaders)
            return;

        var headers = ReadHeaders(workbook, pivotTable);
        if (headers.Count == 0)
            return;

        var bodyStart    = GetPivotBodyStart(sheet, pivotTable, headers);
        var pageStart    = GetPageFieldStart(sheet, pivotTable, headers);
        var rowHdrStart  = GetRowHeaderStart(bodyStart, pivotTable);
        var colHdrStart  = GetColumnHeaderStart(bodyStart, pivotTable);

        AddPageHeaderTargets(sheet, pivotTable, headers, pageStart, targets);
        AddRowHeaderTargets(sheet, pivotTable, headers, rowHdrStart, targets);
        AddColumnHeaderTargets(sheet, pivotTable, headers, colHdrStart, targets);
    }

    private static void AddPageHeaderTargets(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        CellAddress start,
        List<PivotHeaderDropdownTarget> targets)
    {
        if (pivotTable.PageFields.Count == 0)
            return;

        var wrap = Math.Max(0, pivotTable.PageWrap);
        for (var index = 0; index < pivotTable.PageFields.Count; index++)
        {
            var (rowOffset, colPairOffset) = GetPageFieldOffset(
                index, pivotTable.PageFields.Count, wrap, pivotTable.PageOverThenDown);
            var field = pivotTable.PageFields[index];
            var captionAddress = ResolvePageFieldCaptionAddress(
                sheet, pivotTable, headers, field,
                new CellAddress(sheet.Id, start.Row + rowOffset, start.Col + colPairOffset));
            var address = new CellAddress(sheet.Id, captionAddress.Row, captionAddress.Col + 1);
            AddHeaderTarget(sheet, pivotTable, headers, field, PivotHeaderArea.Page, address, targets, allowNonTextValue: true);
        }
    }

    private static void AddRowHeaderTargets(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        CellAddress bodyStart,
        List<PivotHeaderDropdownTarget> targets)
    {
        if (pivotTable.RowFields.Count == 0)
            return;

        if (pivotTable.ReportLayout == PivotReportLayout.Compact && pivotTable.RowFields.Count > 1)
        {
            AddHeaderTarget(sheet, pivotTable, headers, pivotTable.RowFields[0], PivotHeaderArea.Row, bodyStart, targets);
            return;
        }

        for (var index = 0; index < pivotTable.RowFields.Count; index++)
        {
            var address = new CellAddress(sheet.Id, bodyStart.Row, bodyStart.Col + (uint)index);
            AddHeaderTarget(sheet, pivotTable, headers, pivotTable.RowFields[index], PivotHeaderArea.Row, address, targets);
        }
    }

    private static void AddColumnHeaderTargets(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        CellAddress bodyStart,
        List<PivotHeaderDropdownTarget> targets)
    {
        if (pivotTable.ColumnFields.Count == 0)
            return;

        var valueStartCol = bodyStart.Col + (uint)RowFieldOutputColumnCount(pivotTable);
        for (var index = 0; index < pivotTable.ColumnFields.Count; index++)
        {
            var address = new CellAddress(sheet.Id, bodyStart.Row + (uint)index, valueStartCol);
            AddHeaderTarget(sheet, pivotTable, headers, pivotTable.ColumnFields[index], PivotHeaderArea.Column, address, targets);
        }
    }

    private static void AddHeaderTarget(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        PivotFieldModel field,
        PivotHeaderArea area,
        CellAddress address,
        List<PivotHeaderDropdownTarget> targets,
        bool allowNonTextValue = false)
    {
        if (field.ShowDropDowns == false ||
            field.SourceFieldIndex < 0 ||
            field.SourceFieldIndex >= headers.Count ||
            !IsRenderableHeaderCell(sheet, pivotTable, address, allowNonTextValue))
        {
            return;
        }

        var isActive  = IsFieldActive(pivotTable, field);
        var caption   = PivotFieldListPaneBuilder.FieldCaption(headers, field.SourceFieldIndex);
        var menuTarget = new PivotHeaderDropdownTargetModel(
            pivotTable.Name, caption, field.SourceFieldIndex, area, isActive);
        targets.Add(new PivotHeaderDropdownTarget(address, isActive, menuTarget));
    }

    private static bool IsRenderableHeaderCell(
        Sheet sheet, PivotTableModel pivotTable, CellAddress address, bool allowNonTextValue)
    {
        if (sheet.GetCell(address.Row, address.Col)?.Value is not { } value)
            return false;
        if (value is TextValue text)
            return !IsGrandTotalCaption(pivotTable, text.Value);
        return allowNonTextValue;
    }

    private static bool IsFieldActive(PivotTableModel pivotTable, PivotFieldModel field) =>
        HasExplicitSelection(field) ||
        pivotTable.LabelFilters.Any(f => f.SourceFieldIndex == field.SourceFieldIndex) ||
        pivotTable.ValueFilters.Any(f => f.SourceFieldIndex == field.SourceFieldIndex) ||
        pivotTable.Sorts.Any(s => s.FieldIndex == field.SourceFieldIndex);

    private static bool HasExplicitSelection(PivotFieldModel field)
    {
        if (field.SelectedItems is { Count: > 0 } selectedItems)
            return selectedItems.Any(IsExplicitSelection);
        return IsExplicitSelection(field.SelectedItem);
    }

    private static bool IsExplicitSelection(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value, "(All)", StringComparison.OrdinalIgnoreCase);

    // -----------------------------------------------------------------------
    // Row-label expand/collapse adornments
    // -----------------------------------------------------------------------

    /// <param name="collapseState">
    /// Optional session collapse state (R92-app-pivot-drilldown-5-1). When supplied, a row-label
    /// adornment's <see cref="PivotRowLabelAdornment.IsExpanded"/> reflects whether that row's own
    /// item value is currently collapsed for its field; when omitted (the pre-existing default),
    /// every adornment reports expanded, matching this method's original always-expanded behavior.
    /// </param>
    public static IReadOnlyList<PivotRowLabelAdornment> BuildRowLabelAdornments(
        Workbook workbook, Sheet sheet, PivotCollapseState? collapseState = null)
    {
        if (sheet.PivotTables.Count == 0)
            return [];

        var adornments = new List<PivotRowLabelAdornment>();
        foreach (var pivotTable in sheet.PivotTables)
            AddRowLabelAdornments(workbook, sheet, pivotTable, collapseState, adornments);
        return adornments;
    }

    private static void AddRowLabelAdornments(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        PivotCollapseState? collapseState,
        List<PivotRowLabelAdornment> adornments)
    {
        var visibleRange = GetVisiblePivotRange(pivotTable);
        if (pivotTable.RowFields.Count <= 1 || visibleRange.Start.Sheet != sheet.Id)
            return;

        if (pivotTable.ReportLayout != PivotReportLayout.Compact)
        {
            AddNonCompactAdornments(sheet, pivotTable, visibleRange, collapseState, adornments);
            return;
        }

        var labelCol   = visibleRange.Start.Col;
        var dataStartRow = visibleRange.Start.Row + (uint)Math.Max(1, pivotTable.FirstDataRow);
        if (dataStartRow > visibleRange.End.Row)
            return;

        for (var row = dataStartRow; row <= visibleRange.End.Row; row++)
        {
            var address = new CellAddress(sheet.Id, row, labelCol);
            if (!TryGetRowLabel(sheet, pivotTable, address, out var label))
                continue;

            var indentLevel = GetIndentLevel(workbook, sheet, address);
            var showButton  = pivotTable.ShowExpandCollapseButtons &&
                              indentLevel < pivotTable.RowFields.Count - 1 &&
                              NextVisibleLabelIndent(workbook, sheet, pivotTable, row, labelCol) > indentLevel;
            var reservePad  = pivotTable.ShowExpandCollapseButtons && indentLevel > 0;

            if (!showButton && !reservePad)
                continue;

            var isExpanded = !showButton || !IsItemCollapsed(collapseState, pivotTable, indentLevel, label);

            adornments.Add(new PivotRowLabelAdornment(
                address, indentLevel,
                ShowExpandCollapseButton: showButton,
                IsExpanded: isExpanded));
        }
    }

    private static void AddNonCompactAdornments(
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange visibleRange,
        PivotCollapseState? collapseState,
        List<PivotRowLabelAdornment> adornments)
    {
        if (!pivotTable.ShowExpandCollapseButtons)
            return;

        var labelStartCol = visibleRange.Start.Col;
        var dataStartRow  = visibleRange.Start.Row + (uint)Math.Max(1, pivotTable.FirstDataRow);
        if (dataStartRow > visibleRange.End.Row)
            return;

        var parentFieldCount = Math.Max(0, pivotTable.RowFields.Count - 1);
        for (var row = dataStartRow; row <= visibleRange.End.Row; row++)
        for (var level = 0; level < parentFieldCount; level++)
        {
            var labelCol = labelStartCol + (uint)level;
            var address  = new CellAddress(sheet.Id, row, labelCol);
            if (!TryGetRowLabel(sheet, pivotTable, address, out var label))
                continue;

            var hasPrevPeer  = HasSamePrefixOnPreviousRow(sheet, pivotTable, visibleRange, row, labelStartCol, level);
            var hasNextPeer  = HasSamePrefixOnNextRow(sheet, pivotTable, visibleRange, row, labelStartCol, level);
            var hasChildRows = HasChildRowsBeforeNextPeer(sheet, pivotTable, visibleRange, row, labelStartCol, level);
            if (!hasPrevPeer && !hasNextPeer && !hasChildRows)
                continue;

            var showButton = !hasPrevPeer && (hasNextPeer || hasChildRows);
            var isExpanded = !showButton || !IsItemCollapsed(collapseState, pivotTable, level, label);

            adornments.Add(new PivotRowLabelAdornment(
                address, IndentLevel: 0,
                ShowExpandCollapseButton: showButton,
                IsExpanded: isExpanded));
        }
    }

    /// <summary>
    /// True when <paramref name="label"/> (the row-label text at <paramref name="fieldLevel"/>, i.e.
    /// the field's position within <see cref="PivotTableModel.RowFields"/>) is currently collapsed in
    /// <paramref name="collapseState"/> for its underlying source field. Returns <see langword="false"/>
    /// (never collapsed) when no state is supplied or the level does not map to a real row field,
    /// preserving the always-expanded default.
    /// </summary>
    private static bool IsItemCollapsed(
        PivotCollapseState? collapseState, PivotTableModel pivotTable, int fieldLevel, string label)
    {
        if (collapseState is null || fieldLevel < 0 || fieldLevel >= pivotTable.RowFields.Count)
            return false;

        var field = pivotTable.RowFields[fieldLevel];
        return collapseState.IsCollapsed(pivotTable.Name, field.SourceFieldIndex, label);
    }

    // -----------------------------------------------------------------------
    // Shared coordinate helpers
    // -----------------------------------------------------------------------

    private static CellAddress GetPageFieldStart(Sheet sheet, PivotTableModel pivotTable, IReadOnlyList<string> headers)
    {
        var start = pivotTable.TargetRange.Start;
        if (pivotTable.PageFields.Count == 0 || IsPageFieldCaption(sheet, start, pivotTable, headers))
            return start;

        var pageFieldRows = GetPageFieldRowSpan(pivotTable);
        if (start.Row <= pageFieldRows + 1)
            return start;

        var nativePageStart = new CellAddress(start.Sheet, start.Row - pageFieldRows - 1, start.Col);
        return IsPageFieldCaption(sheet, nativePageStart, pivotTable, headers) ? nativePageStart : start;
    }

    private static CellAddress GetPivotBodyStart(Sheet sheet, PivotTableModel pivotTable, IReadOnlyList<string> headers)
    {
        var start = pivotTable.TargetRange.Start;
        var pageFieldRows = GetPageFieldRowSpan(pivotTable);
        return pageFieldRows == 0 || !IsPageFieldCaption(sheet, start, pivotTable, headers)
            ? start
            : new CellAddress(start.Sheet, start.Row + pageFieldRows + 1, start.Col);
    }

    private static CellAddress GetRowHeaderStart(CellAddress bodyStart, PivotTableModel pivotTable) =>
        new(bodyStart.Sheet, bodyStart.Row + (uint)Math.Max(0, pivotTable.FirstDataRow - 1), bodyStart.Col);

    private static CellAddress GetColumnHeaderStart(CellAddress bodyStart, PivotTableModel pivotTable) =>
        new(bodyStart.Sheet, bodyStart.Row + (uint)Math.Max(0, pivotTable.FirstHeaderRow - 1), bodyStart.Col);

    private static uint GetPageFieldRowSpan(PivotTableModel pivotTable)
    {
        var count = pivotTable.PageFields.Count;
        if (count == 0) return 0;
        var wrap = Math.Max(0, pivotTable.PageWrap);
        if (pivotTable.PageOverThenDown)
            return (uint)(wrap <= 0 ? 1 : (int)Math.Ceiling(count / (double)wrap));
        return (uint)(wrap <= 0 ? count : Math.Min(count, wrap));
    }

    private static (uint RowOffset, uint ColPairOffset) GetPageFieldOffset(
        int index, int pageFieldCount, int wrap, bool overThenDown)
    {
        if (overThenDown)
        {
            var fieldsPerRow = wrap <= 0 ? pageFieldCount : wrap;
            return ((uint)(index / fieldsPerRow), (uint)((index % fieldsPerRow) * 2));
        }
        var rowsPerColumn = wrap <= 0 ? pageFieldCount : wrap;
        return ((uint)(index % rowsPerColumn), (uint)((index / rowsPerColumn) * 2));
    }

    private static CellAddress ResolvePageFieldCaptionAddress(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        PivotFieldModel field,
        CellAddress expectedCaptionAddress)
    {
        var caption = field.SourceFieldIndex >= 0 && field.SourceFieldIndex < headers.Count
            ? PivotFieldListPaneBuilder.FieldCaption(headers, field.SourceFieldIndex)
            : null;
        if (string.IsNullOrWhiteSpace(caption) || CellTextEquals(sheet, expectedCaptionAddress, caption))
            return expectedCaptionAddress;

        var pageStart   = GetPageFieldStart(sheet, pivotTable, headers);
        var pageRows    = Math.Max(1u, GetPageFieldRowSpan(pivotTable));
        var maxCol      = Math.Max(
            pivotTable.TargetRange.End.Col + 1,
            pageStart.Col + (uint)(pivotTable.PageFields.Count * 3));

        for (var row = pageStart.Row; row < pageStart.Row + pageRows; row++)
        for (var col = pageStart.Col; col <= maxCol; col++)
        {
            var address = new CellAddress(sheet.Id, row, col);
            if (CellTextEquals(sheet, address, caption))
                return address;
        }
        return expectedCaptionAddress;
    }

    private static bool IsPageFieldCaption(
        Sheet sheet, CellAddress address, PivotTableModel pivotTable, IReadOnlyList<string> headers)
    {
        if (pivotTable.PageFields.Count == 0 ||
            sheet.GetCell(address.Row, address.Col)?.Value is not TextValue text)
        {
            return false;
        }
        var firstPageField = pivotTable.PageFields[0];
        return firstPageField.SourceFieldIndex >= 0 &&
               firstPageField.SourceFieldIndex < headers.Count &&
               string.Equals(
                   text.Value,
                   PivotFieldListPaneBuilder.FieldCaption(headers, firstPageField.SourceFieldIndex),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool CellTextEquals(Sheet sheet, CellAddress address, string text) =>
        sheet.GetCell(address.Row, address.Col)?.Value is TextValue cellText &&
        string.Equals(cellText.Value, text, StringComparison.OrdinalIgnoreCase);

    private static bool IsGrandTotalCaption(PivotTableModel pivotTable, string text) =>
        string.Equals(
            text,
            string.IsNullOrWhiteSpace(pivotTable.GrandTotalCaption) ? "Grand Total" : pivotTable.GrandTotalCaption!,
            StringComparison.CurrentCultureIgnoreCase);

    private static int RowFieldOutputColumnCount(PivotTableModel pivotTable) =>
        pivotTable.ReportLayout == PivotReportLayout.Compact && pivotTable.RowFields.Count > 1
            ? 1
            : pivotTable.RowFields.Count;

    // -----------------------------------------------------------------------
    // Row-label adornment helpers
    // -----------------------------------------------------------------------

    private static bool HasChildRowsBeforeNextPeer(
        Sheet sheet, PivotTableModel pivotTable, GridRange visibleRange,
        uint row, uint labelStartCol, int level)
    {
        if (level + 1 >= pivotTable.RowFields.Count) return false;
        var labelCol = labelStartCol + (uint)level;
        var childCol = labelCol + 1;
        for (var nextRow = row + 1; nextRow <= visibleRange.End.Row; nextRow++)
        {
            if (TryGetRowLabel(sheet, pivotTable, new CellAddress(sheet.Id, nextRow, labelCol), out _))
                return false;
            if (TryGetRowLabel(sheet, pivotTable, new CellAddress(sheet.Id, nextRow, childCol), out _))
                return true;
        }
        return false;
    }

    private static bool HasSamePrefixOnPreviousRow(
        Sheet sheet, PivotTableModel pivotTable, GridRange visibleRange,
        uint row, uint labelStartCol, int level) =>
        row > visibleRange.Start.Row && HasSamePrefix(sheet, pivotTable, row, row - 1, labelStartCol, level);

    private static bool HasSamePrefixOnNextRow(
        Sheet sheet, PivotTableModel pivotTable, GridRange visibleRange,
        uint row, uint labelStartCol, int level) =>
        row < visibleRange.End.Row && HasSamePrefix(sheet, pivotTable, row, row + 1, labelStartCol, level);

    private static bool HasSamePrefix(
        Sheet sheet, PivotTableModel pivotTable,
        uint row, uint otherRow, uint labelStartCol, int level)
    {
        for (var offset = 0; offset <= level; offset++)
        {
            var col = labelStartCol + (uint)offset;
            if (!TryGetRowLabel(sheet, pivotTable, new CellAddress(sheet.Id, row, col), out var text) ||
                !TryGetRowLabel(sheet, pivotTable, new CellAddress(sheet.Id, otherRow, col), out var otherText) ||
                !string.Equals(text, otherText, StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private static int NextVisibleLabelIndent(
        Workbook workbook, Sheet sheet, PivotTableModel pivotTable, uint row, uint labelCol)
    {
        for (var nextRow = row + 1; nextRow <= pivotTable.TargetRange.End.Row; nextRow++)
        {
            var nextAddress = new CellAddress(sheet.Id, nextRow, labelCol);
            if (!TryGetRowLabel(sheet, pivotTable, nextAddress, out _))
                continue;
            return GetIndentLevel(workbook, sheet, nextAddress);
        }
        return -1;
    }

    private static bool TryGetRowLabel(Sheet sheet, PivotTableModel pivotTable, CellAddress address, out string text)
    {
        text = "";
        if (sheet.GetCell(address.Row, address.Col)?.Value is not TextValue value ||
            string.IsNullOrWhiteSpace(value.Value))
        {
            return false;
        }
        text = value.Value;
        return !IsGrandTotalCaption(pivotTable, text);
    }

    private static int GetIndentLevel(Workbook workbook, Sheet sheet, CellAddress address)
    {
        var cell = sheet.GetCell(address.Row, address.Col);
        return cell is null ? 0 : Math.Clamp(workbook.GetStyle(cell.StyleId).IndentLevel, 0, 15);
    }

    private static GridRange GetVisiblePivotRange(PivotTableModel pivotTable) =>
        pivotTable.LastRenderedRange is { } lastRenderedRange &&
        lastRenderedRange.Start.Sheet == pivotTable.TargetRange.Start.Sheet
            ? lastRenderedRange
            : pivotTable.TargetRange;

    private static List<string> ReadHeaders(Workbook workbook, PivotTableModel pivotTable)
    {
        var headers = new List<string>();
        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is not null)
        {
            for (var col = pivotTable.SourceRange.Start.Col; col <= pivotTable.SourceRange.End.Col; col++)
            {
                var value = sourceSheet.GetCell(pivotTable.SourceRange.Start.Row, col)?.Value;
                headers.Add(value is TextValue text && !string.IsNullOrWhiteSpace(text.Value)
                    ? text.Value
                    : $"Field{headers.Count + 1}");
            }
        }

        return PivotSourceHeaderResolver.Resolve(workbook, pivotTable, headers);
    }
}
