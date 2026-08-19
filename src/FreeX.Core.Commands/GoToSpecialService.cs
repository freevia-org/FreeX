using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum GoToSpecialKind
{
    Blanks,
    Constants,
    Formulas,
    Comments,
    DataValidation,
    VisibleCellsOnly,
    RowDifferences,
    ColumnDifferences,
    CurrentRegion,
    LastCell,
    ConditionalFormats,
    Objects,
    Precedents,
    Dependents,
    CurrentArray
}

[Flags]
public enum GoToSpecialValueTypes
{
    None = 0,
    Numbers = 1,
    Text = 2,
    Logicals = 4,
    Errors = 8,
    All = Numbers | Text | Logicals | Errors
}

public sealed record GoToSpecialOptions(
    GoToSpecialValueTypes ValueTypes = GoToSpecialValueTypes.All,
    // Excel's Go To Special dialog offers "All" vs. "Same as active cell" sub-options for
    // both Conditional Formats and Data Validation. When true, only cells governed by the
    // SAME specific rule(s) as the active cell are matched, rather than every rule overlapping
    // the search range.
    bool MatchActiveCellRuleOnly = false,
    // Excel's Go To Special dialog offers "Direct Only" (default) vs. "All Levels" sub-options
    // for both Precedents and Dependents. When true, the trace is transitive: precedents-of-
    // precedents (or dependents-of-dependents) are included too, not just the one direct hop.
    bool AllLevels = false);

public static class GoToSpecialService
{
    private const int ColumnKeyBits = 15;
    private const int MinimumRuleRangesForIndex = 8;
    private const long MaximumIndexedRuleCells = 250_000;
    private const long MaximumDirectScanCells = 1_000_000;

    public static IReadOnlyList<CellAddress> Find(
        Sheet sheet,
        GridRange range,
        GoToSpecialKind kind,
        CellAddress? activeCell = null,
        GoToSpecialOptions? options = null)
        => Find(null, sheet, range, kind, activeCell, options);

    public static IReadOnlyList<CellAddress> Find(
        Workbook? workbook,
        Sheet sheet,
        GridRange range,
        GoToSpecialKind kind,
        CellAddress? activeCell = null,
        GoToSpecialOptions? options = null)
    {
        options ??= new GoToSpecialOptions();

        if (kind == GoToSpecialKind.CurrentRegion)
            return SelectionRangeService.GetCurrentRegion(sheet, activeCell ?? range.Start) is { } currentRegion
                ? MaterializeCells(currentRegion)
                : [];

        if (kind == GoToSpecialKind.LastCell)
            return sheet.GetUsedRange() is { } usedRange ? [usedRange.End] : [];

        if (kind == GoToSpecialKind.CurrentArray)
            return FindCurrentArray(sheet, activeCell ?? range.Start);

        if (kind == GoToSpecialKind.Objects)
            return FindObjects(sheet, range);

        if (kind == GoToSpecialKind.Precedents)
            return workbook is null ? [] : FindPrecedents(workbook, sheet, range, options.AllLevels);

        if (kind == GoToSpecialKind.Dependents)
            return workbook is null ? [] : FindDependents(workbook, sheet, range, options.AllLevels);

        var result = new List<CellAddress>();
        if (kind == GoToSpecialKind.RowDifferences)
            return FindRowDifferences(sheet, range, activeCell);
        if (kind == GoToSpecialKind.ColumnDifferences)
            return FindColumnDifferences(sheet, range, activeCell);
        if (kind == GoToSpecialKind.DataValidation)
            return FindDataValidations(sheet.DataValidations, range, options.MatchActiveCellRuleOnly ? activeCell : null);
        if (kind == GoToSpecialKind.ConditionalFormats)
            return FindConditionalFormats(sheet.ConditionalFormats, range, options.MatchActiveCellRuleOnly ? activeCell : null);

        var scanRange = range;
        // Only an explicit whole-row/whole-column/whole-sheet selection (e.g. Ctrl+A twice, then
        // Go To Special) reaches all the way to the sheet's nominal boundary on at least one axis
        // -- that selection nominally spans up to ~17 billion cells. Real Excel always intersects
        // Go To Special's search with the sheet's actual used range in that case rather than
        // scanning the full nominal grid, so do the same here -- otherwise this becomes an
        // effectively unbounded per-cell scan regardless of how little data the sheet has. A fully
        // bounded selection the user explicitly drew (e.g. A1:D300000) never hits the sheet's
        // boundary and must be scanned in full, however large, so it is not clamped to the used
        // range -- clamping it would silently drop legitimate matches (e.g. blanks) outside the
        // used range but still inside the user's own selection.
        var isUnboundedSelection = scanRange.End.Row == CellAddress.MaxRow || scanRange.End.Col == CellAddress.MaxCol;
        if (isUnboundedSelection && scanRange.CellCount > MaximumDirectScanCells)
        {
            if (sheet.GetUsedRange() is not { } usedRange || !GridRange.TryIntersect(scanRange, usedRange, out scanRange))
                return result;
        }

        foreach (var address in scanRange.AllCells())
        {
            if (kind == GoToSpecialKind.VisibleCellsOnly)
            {
                if (!sheet.IsRowEffectivelyHidden(address.Row) &&
                    !sheet.IsColEffectivelyHidden(address.Col))
                {
                    result.Add(address);
                }
                continue;
            }

            var cell = sheet.GetCell(address);
            switch (kind)
            {
                // Use Sheet.GetValue rather than cell?.Value: a non-anchor dynamic-array spill
                // member has no _cells entry of its own (its value lives only in the spill
                // overlay), so `cell is null` here would wrongly mark every visibly-populated
                // spill member as blank. GetValue is the documented "value question" API that
                // resolves the spill overlay; GetCell/cell.Value stays storage-only for the other
                // cases below (HasFormula, etc.) which genuinely need the Cell object.
                case GoToSpecialKind.Blanks when sheet.GetValue(address) is BlankValue:
                    result.Add(address);
                    break;
                case GoToSpecialKind.Constants when cell is { HasFormula: false } &&
                    cell.Value is not BlankValue &&
                    MatchesValueType(cell.Value, options.ValueTypes):
                    result.Add(address);
                    break;
                case GoToSpecialKind.Formulas when cell?.HasFormula == true &&
                    MatchesValueType(cell.Value, options.ValueTypes):
                    result.Add(address);
                    break;
                case GoToSpecialKind.Comments when sheet.Comments.ContainsKey(address) || sheet.ThreadedComments.ContainsKey(address):
                    result.Add(address);
                    break;
            }
        }

        return result;
    }

    private static bool MatchesValueType(ScalarValue value, GoToSpecialValueTypes valueTypes) =>
        value switch
        {
            NumberValue or DateTimeValue => (valueTypes & GoToSpecialValueTypes.Numbers) != 0,
            TextValue => (valueTypes & GoToSpecialValueTypes.Text) != 0,
            BoolValue => (valueTypes & GoToSpecialValueTypes.Logicals) != 0,
            ErrorValue => (valueTypes & GoToSpecialValueTypes.Errors) != 0,
            _ => false
        };

    private static List<CellAddress> MaterializeCells(GridRange range)
    {
        var capacity = range.CellCount <= int.MaxValue ? (int)range.CellCount : 0;
        var cells = capacity > 0 ? new List<CellAddress>(capacity) : [];
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                cells.Add(new CellAddress(range.Start.Sheet, row, col));
        }

        return cells;
    }

    private static bool HasConditionalFormatAt(List<ConditionalFormat> rules, CellAddress address)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i].AppliesTo.Contains(address))
                return true;
        }

        return false;
    }

    private static bool HasDataValidationAt(List<DataValidation> rules, CellAddress address)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule.AppliesTo.Contains(address))
                return true;

            var additionalRanges = rule.AdditionalRanges;
            for (var rangeIndex = 0; rangeIndex < additionalRanges.Count; rangeIndex++)
            {
                if (additionalRanges[rangeIndex].Contains(address))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Excel's "Same as active cell" sub-option for Go To Special > Conditional Formats:
    /// selects only cells governed by the SAME specific rule(s) that apply to
    /// <paramref name="activeCell"/> -- not every rule intersecting <paramref name="range"/>
    /// (that broader behavior is "All", the default).
    /// </summary>
    private static IReadOnlyList<CellAddress> FindConditionalFormatsMatchingActiveCell(
        List<ConditionalFormat> rules,
        GridRange range,
        CellAddress activeCell)
    {
        List<ConditionalFormat>? matchingRules = null;
        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i].AppliesTo.Contains(activeCell))
                (matchingRules ??= []).Add(rules[i]);
        }

        if (matchingRules is null)
            return [];

        var result = new List<CellAddress>();
        foreach (var address in range.AllCells())
        {
            if (HasConditionalFormatAt(matchingRules, address))
                result.Add(address);
        }

        return result;
    }

    /// <summary>
    /// Excel's "Same as active cell" sub-option for Go To Special > Data Validation:
    /// selects only cells governed by the SAME specific rule(s) that apply to
    /// <paramref name="activeCell"/> -- not every rule intersecting <paramref name="range"/>
    /// (that broader behavior is "All", the default).
    /// </summary>
    private static IReadOnlyList<CellAddress> FindDataValidationsMatchingActiveCell(
        List<DataValidation> rules,
        GridRange range,
        CellAddress activeCell)
    {
        List<DataValidation>? matchingRules = null;
        for (var i = 0; i < rules.Count; i++)
        {
            if (HasDataValidationAt([rules[i]], activeCell))
                (matchingRules ??= []).Add(rules[i]);
        }

        if (matchingRules is null)
            return [];

        var result = new List<CellAddress>();
        foreach (var address in range.AllCells())
        {
            if (HasDataValidationAt(matchingRules, address))
                result.Add(address);
        }

        return result;
    }

    private static IReadOnlyList<CellAddress> FindConditionalFormats(
        List<ConditionalFormat> rules,
        GridRange range,
        CellAddress? matchActiveCellOnly = null)
    {
        if (rules.Count == 0)
            return [];

        if (matchActiveCellOnly is { } activeCell)
            return FindConditionalFormatsMatchingActiveCell(rules, range, activeCell);

        var (coversRange, coveredCells, rangeCount) = AnalyzeConditionalFormatRanges(rules, range);
        if (coversRange)
            return MaterializeCells(range);

        if (ShouldIndexRuleRanges(rangeCount, coveredCells))
        {
            var matches = BuildConditionalFormatAddressKeys(rules, range, coveredCells);
            return MaterializeMatches(range, matches);
        }

        var result = new List<CellAddress>();
        foreach (var address in range.AllCells())
        {
            if (HasConditionalFormatAt(rules, address))
                result.Add(address);
        }

        return result;
    }

    private static IReadOnlyList<CellAddress> FindDataValidations(
        List<DataValidation> rules,
        GridRange range,
        CellAddress? matchActiveCellOnly = null)
    {
        if (rules.Count == 0)
            return [];

        if (matchActiveCellOnly is { } activeCell)
            return FindDataValidationsMatchingActiveCell(rules, range, activeCell);

        var (coversRange, coveredCells, rangeCount) = AnalyzeDataValidationRanges(rules, range);
        if (coversRange)
            return MaterializeCells(range);

        if (ShouldIndexRuleRanges(rangeCount, coveredCells))
        {
            var matches = BuildDataValidationAddressKeys(rules, range, coveredCells);
            return MaterializeMatches(range, matches);
        }

        var result = new List<CellAddress>();
        foreach (var address in range.AllCells())
        {
            if (HasDataValidationAt(rules, address))
                result.Add(address);
        }

        return result;
    }

    private static bool ShouldIndexRuleRanges(int rangeCount, long coveredCells) =>
        rangeCount >= MinimumRuleRangesForIndex &&
        coveredCells > 0 &&
        coveredCells <= MaximumIndexedRuleCells;

    private static (bool CoversRange, long CoveredCells, int RangeCount) AnalyzeConditionalFormatRanges(
        List<ConditionalFormat> rules,
        GridRange searchRange)
    {
        long coveredCells = 0;
        for (var i = 0; i < rules.Count; i++)
        {
            var ruleRange = rules[i].AppliesTo;
            if (ContainsRange(ruleRange, searchRange))
                return (true, coveredCells, i + 1);

            if (TryIntersect(ruleRange, searchRange, out var intersection))
                coveredCells += intersection.CellCount;
        }

        return (false, coveredCells, rules.Count);
    }

    private static (bool CoversRange, long CoveredCells, int RangeCount) AnalyzeDataValidationRanges(
        List<DataValidation> rules,
        GridRange searchRange)
    {
        long coveredCells = 0;
        var rangeCount = 0;
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            rangeCount++;
            if (ContainsRange(rule.AppliesTo, searchRange))
                return (true, coveredCells, rangeCount);

            if (TryIntersect(rule.AppliesTo, searchRange, out var intersection))
                coveredCells += intersection.CellCount;

            var additionalRanges = rule.AdditionalRanges;
            for (var rangeIndex = 0; rangeIndex < additionalRanges.Count; rangeIndex++)
            {
                rangeCount++;
                var additionalRange = additionalRanges[rangeIndex];
                if (ContainsRange(additionalRange, searchRange))
                    return (true, coveredCells, rangeCount);

                if (TryIntersect(additionalRange, searchRange, out intersection))
                    coveredCells += intersection.CellCount;
            }
        }

        return (false, coveredCells, rangeCount);
    }

    private static List<ulong> BuildConditionalFormatAddressKeys(
        List<ConditionalFormat> rules,
        GridRange searchRange,
        long coveredCells)
    {
        var matches = new List<ulong>(GetListCapacity(coveredCells));
        for (var i = 0; i < rules.Count; i++)
        {
            if (TryIntersect(rules[i].AppliesTo, searchRange, out var intersection))
                AddCells(matches, intersection);
        }

        return matches;
    }

    private static List<ulong> BuildDataValidationAddressKeys(
        List<DataValidation> rules,
        GridRange searchRange,
        long coveredCells)
    {
        var matches = new List<ulong>(GetListCapacity(coveredCells));
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (TryIntersect(rule.AppliesTo, searchRange, out var intersection))
                AddCells(matches, intersection);

            var additionalRanges = rule.AdditionalRanges;
            for (var rangeIndex = 0; rangeIndex < additionalRanges.Count; rangeIndex++)
            {
                if (TryIntersect(additionalRanges[rangeIndex], searchRange, out intersection))
                    AddCells(matches, intersection);
            }
        }

        return matches;
    }

    private static int GetListCapacity(long coveredCells) =>
        coveredCells <= int.MaxValue ? (int)coveredCells : 0;

    private static void AddCells(List<ulong> matches, GridRange range)
    {
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                matches.Add(CreateAddressKey(row, col));
        }
    }

    private static IReadOnlyList<CellAddress> MaterializeMatches(
        GridRange range,
        List<ulong> matches)
    {
        if (matches.Count == 0)
            return [];

        var result = new List<CellAddress>(matches.Count);
        matches.Sort();
        ulong previousKey = 0;
        for (var index = 0; index < matches.Count; index++)
        {
            var key = matches[index];
            if (index > 0 && key == previousKey)
                continue;

            previousKey = key;
            result.Add(new CellAddress(range.Start.Sheet, GetAddressKeyRow(key), GetAddressKeyColumn(key)));
        }

        return result;
    }

    private static ulong CreateAddressKey(uint row, uint col) =>
        ((ulong)row << ColumnKeyBits) | col;

    private static uint GetAddressKeyRow(ulong key) =>
        (uint)(key >> ColumnKeyBits);

    private static uint GetAddressKeyColumn(ulong key) =>
        (uint)(key & ((1u << ColumnKeyBits) - 1));

    private static bool ContainsRange(GridRange outer, GridRange inner) =>
        outer.Contains(inner);

    private static bool TryIntersect(GridRange first, GridRange second, out GridRange intersection) =>
        GridRange.TryIntersect(first, second, out intersection);

    private static IReadOnlyList<CellAddress> FindObjects(Sheet sheet, GridRange range)
    {
        var result = new List<CellAddress>();
        foreach (var chart in sheet.Charts)
            AddIfInRange(result, range, ResolveChartAnchor(sheet, chart));
        foreach (var shape in sheet.DrawingShapes)
            AddIfInRange(result, range, shape.Anchor);
        foreach (var picture in sheet.Pictures)
            AddIfInRange(result, range, picture.Anchor);
        foreach (var textBox in sheet.TextBoxes)
            AddIfInRange(result, range, textBox.Anchor);
        foreach (var control in sheet.FormControls)
        {
            // A form control's anchor is a full GridRange (unlike the single-CellAddress
            // anchors above), so it must be selected whenever ANY part of its extent
            // overlaps the search range -- not just when its start cell happens to fall
            // inside it -- to match Excel's bounding-box-intersection behavior.
            if (control.Anchor is { } controlAnchor && controlAnchor.Overlaps(range) &&
                !result.Contains(controlAnchor.Start))
                result.Add(controlAnchor.Start);
        }

        return result;
    }

    /// <summary>
    /// Resolves a chart's actual on-screen anchor cell from its stored pixel position
    /// (<see cref="ChartModel.Left"/>/<see cref="ChartModel.Top"/>), NOT its data-source range
    /// (<see cref="ChartModel.DataRange"/>) -- a chart built from one range can be freely dragged
    /// anywhere on the sheet, and Go To Special > Objects must select/omit it by where it visually
    /// sits, matching Excel. This walks cumulative column widths/row heights from the sheet origin,
    /// the same pixel model <see cref="FreeX.Core.IO.XlsxDrawingAnchorApplier"/> uses to compute
    /// Left/Top when loading a chart's anchor off disk, just inverted.
    /// </summary>
    private static CellAddress ResolveChartAnchor(Sheet sheet, ChartModel chart)
    {
        var col = FindColumnAtPixel(sheet, chart.Left);
        var row = FindRowAtPixel(sheet, chart.Top);
        return new CellAddress(chart.DataRange.Start.Sheet, row, col);
    }

    private static uint FindColumnAtPixel(Sheet sheet, double targetPixel)
    {
        var cumulative = 0.0;
        for (var col = 1u; col < CellAddress.MaxCol; col++)
        {
            if (sheet.IsColEffectivelyHidden(col))
                continue;

            var width = sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth) * 8;
            if (cumulative + width > targetPixel)
                return col;

            cumulative += width;
        }

        return CellAddress.MaxCol;
    }

    private static uint FindRowAtPixel(Sheet sheet, double targetPixel)
    {
        var cumulative = 0.0;
        for (var row = 1u; row < CellAddress.MaxRow; row++)
        {
            if (sheet.IsRowEffectivelyHidden(row))
                continue;

            var height = sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight);
            if (cumulative + height > targetPixel)
                return row;

            cumulative += height;
        }

        return CellAddress.MaxRow;
    }

    /// <summary>
    /// Excel's Go To Special > Current Array: from any member (or the anchor) of a legacy CSE
    /// array formula or a dynamic-array spill, selects the whole array's extent.
    /// </summary>
    private static IReadOnlyList<CellAddress> FindCurrentArray(Sheet sheet, CellAddress activeCell)
    {
        if (!sheet.TryGetArrayExtent(activeCell, out var anchor, out var rows, out var cols))
            return [];

        return MaterializeCells(new GridRange(
            anchor,
            new CellAddress(anchor.Sheet, anchor.Row + rows - 1, anchor.Col + cols - 1)));
    }

    /// <summary>
    /// Excel's Go To Special > Precedents "Direct Only" (default) vs. "All Levels" sub-option:
    /// when <paramref name="allLevels"/> is false, only cells directly referenced by a formula
    /// in <paramref name="range"/> are matched. When true, the trace is transitive -- precedents
    /// of precedents are followed until no new cells are discovered (a formula chain A1 &lt;- B1 &lt;-
    /// C1 selected from C1 selects both B1 and A1).
    /// </summary>
    private static IReadOnlyList<CellAddress> FindPrecedents(Workbook workbook, Sheet sheet, GridRange range, bool allLevels)
    {
        var result = new List<CellAddress>();
        var visited = new HashSet<CellAddress>();
        var queue = new Queue<CellAddress>();
        foreach (var address in range.AllCells())
        {
            if (visited.Add(address))
                queue.Enqueue(address);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var precedent in FormulaAuditingService.GetDirectPrecedents(workbook, current))
            {
                if (precedent.Sheet == sheet.Id && !result.Contains(precedent))
                    result.Add(precedent);

                // Only follow the chain further when "All Levels" was requested; otherwise the
                // queue holds nothing beyond the original range cells and drains after one hop.
                if (allLevels && visited.Add(precedent))
                    queue.Enqueue(precedent);
            }
        }

        return result;
    }

    /// <summary>
    /// Excel's Go To Special > Dependents "Direct Only" (default) vs. "All Levels" sub-option:
    /// when <paramref name="allLevels"/> is false, only cells whose formula directly references
    /// a cell in <paramref name="range"/> are matched. When true, the trace is transitive --
    /// dependents of dependents are followed until no new cells are discovered.
    /// </summary>
    private static IReadOnlyList<CellAddress> FindDependents(Workbook workbook, Sheet sheet, GridRange range, bool allLevels)
    {
        var result = new List<CellAddress>();
        var visited = new HashSet<CellAddress>();
        var queue = new Queue<GridRange>();
        queue.Enqueue(range);

        // "All Levels" grows a BFS frontier where every dequeued cell issues its own dependents
        // lookup. Building the reverse-dependency index once up front -- instead of calling
        // FormulaAuditingService.GetDirectDependents (a full workbook re-scan/re-parse) for every
        // dequeued cell -- turns the traversal from O(chain-length * total-formula-count) full
        // re-parses into one O(total-formula-count) parse followed by O(chain-length) cheap index
        // lookups (R123-core-commands-formula-auditing-all-levels-perf). The "Direct Only" case
        // only ever dequeues once, so it keeps calling the public single-shot API directly rather
        // than paying to build an index it will use exactly once.
        var index = allLevels ? FormulaAuditingService.BuildDependentsIndex(workbook) : null;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var dependents = index is not null
                ? FormulaAuditingService.GetDirectDependents(workbook, index, current)
                : FormulaAuditingService.GetDirectDependents(workbook, current);

            foreach (var dependent in dependents)
            {
                if (dependent.Sheet == sheet.Id && !result.Contains(dependent))
                    result.Add(dependent);

                if (allLevels && visited.Add(dependent))
                    queue.Enqueue(new GridRange(dependent, dependent));
            }

            if (!allLevels)
                break;
        }

        return result;
    }

    private static void AddIfInRange(List<CellAddress> result, GridRange range, CellAddress address)
    {
        if (range.Contains(address) && !result.Contains(address))
            result.Add(address);
    }

    private static IReadOnlyList<CellAddress> FindRowDifferences(Sheet sheet, GridRange range, CellAddress? activeCell)
    {
        // Excel always compares each row against the cell in the ACTIVE cell's column,
        // not the selection's top-left column. Fall back to the top-left column when
        // there is no active cell or it falls outside the searched range.
        var baseCol = activeCell is { } cell && range.Contains(cell) ? cell.Col : range.Start.Col;

        var result = new List<CellAddress>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            // GetValue (not GetCell?.Value) so a dynamic-array spill member's real displayed
            // value participates in the comparison instead of silently reading back as blank.
            var baseValue = sheet.GetValue(row, baseCol);
            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                if (col == baseCol)
                    continue;

                var address = new CellAddress(range.Start.Sheet, row, col);
                var value = sheet.GetValue(address);
                if (!ScalarEquals(baseValue, value))
                    result.Add(address);
            }
        }

        return result;
    }

    private static IReadOnlyList<CellAddress> FindColumnDifferences(Sheet sheet, GridRange range, CellAddress? activeCell)
    {
        // Excel always compares each column against the cell in the ACTIVE cell's row,
        // not the selection's top-left row. Fall back to the top-left row when there is
        // no active cell or it falls outside the searched range.
        var baseRow = activeCell is { } cell && range.Contains(cell) ? cell.Row : range.Start.Row;

        var result = new List<CellAddress>();
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            // GetValue (not GetCell?.Value) so a dynamic-array spill member's real displayed
            // value participates in the comparison instead of silently reading back as blank.
            var baseValue = sheet.GetValue(baseRow, col);
            for (var row = range.Start.Row; row <= range.End.Row; row++)
            {
                if (row == baseRow)
                    continue;

                var address = new CellAddress(range.Start.Sheet, row, col);
                var value = sheet.GetValue(address);
                if (!ScalarEquals(baseValue, value))
                    result.Add(address);
            }
        }

        return result;
    }

    private static bool ScalarEquals(ScalarValue a, ScalarValue b) =>
        (a, b) switch
        {
            (TextValue ta, TextValue tb) => string.Equals(ta.Value, tb.Value, StringComparison.OrdinalIgnoreCase),
            (NumberValue na, NumberValue nb) => na.Value.Equals(nb.Value),
            (DateTimeValue da, DateTimeValue db) => da.Value.Equals(db.Value),
            (BoolValue ba, BoolValue bb) => ba.Value == bb.Value,
            (BlankValue, BlankValue) => true,
            _ => false
        };
}
