using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    internal static AddressBearingStateSnapshot CaptureAddressBearingState(Workbook workbook, Sheet sheet) =>
        new(
            CaptureStyleOnlyEntries(sheet),
            CaptureUIntIntDictionary(sheet.RowOutlineLevels),
            CaptureUIntIntDictionary(sheet.ColOutlineLevels),
            CaptureUIntSet(sheet.GroupHiddenRows),
            CaptureUIntSet(sheet.GroupHiddenCols),
            CaptureList(sheet.AllowEditRanges),
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            ClonePageBreaksMetadata(sheet.RowPageBreaksMetadata),
            ClonePageBreaksMetadata(sheet.ColumnPageBreaksMetadata),
            CaptureList(workbook.WatchedCells),
            CloneCellWatchesMetadata(sheet.CellWatchesMetadata),
            CloneIgnoredErrorsMetadata(sheet.IgnoredErrorsMetadata),
            sheet.AutoFilter,
            sheet.SmartTags,
            sheet.DataConsolidation,
            sheet.SortState,
            sheet.SingleXmlCells,
            CaptureTextBoxes(sheet),
            CaptureDrawingShapes(sheet),
            CapturePictures(sheet),
            CaptureSparklines(sheet),
            CapturePivotTables(sheet),
            CaptureList(sheet.StructuredTables),
            CapturePivotCaches(workbook),
            CaptureList(workbook.Scenarios),
            CaptureFormControls(sheet));

    private static IReadOnlyList<StyleOnlyEntry> CaptureStyleOnlyEntries(Sheet sheet)
    {
        if (!sheet.HasStyleOnlyCells)
            return [];

        var entries = new List<StyleOnlyEntry>(sheet.StyleOnlyCellCount);
        foreach (var entry in sheet.GetStyleOnlyEntries())
            entries.Add(new StyleOnlyEntry(entry.Key.Row, entry.Key.Col, entry.StyleId));

        return entries;
    }

    private static IReadOnlyDictionary<uint, int> CaptureUIntIntDictionary(IReadOnlyDictionary<uint, int> source) =>
        source.Count == 0 ? EmptyUIntIntDictionary.Instance : new Dictionary<uint, int>(source);

    private static IReadOnlyCollection<uint> CaptureUIntSet(IReadOnlyCollection<uint> source) =>
        source.Count == 0 ? [] : [.. source];

    private static IReadOnlyList<T> CaptureList<T>(IReadOnlyCollection<T> source) =>
        source.Count == 0 ? [] : [.. source];

    private static IReadOnlyList<TextBoxAddressSnapshot> CaptureTextBoxes(Sheet sheet)
    {
        if (sheet.TextBoxes.Count == 0)
            return [];

        var snapshots = new List<TextBoxAddressSnapshot>(sheet.TextBoxes.Count);
        foreach (var textBox in sheet.TextBoxes)
            snapshots.Add(new TextBoxAddressSnapshot(textBox, textBox.Anchor));

        return snapshots;
    }

    private static IReadOnlyList<DrawingShapeAddressSnapshot> CaptureDrawingShapes(Sheet sheet)
    {
        if (sheet.DrawingShapes.Count == 0)
            return [];

        var snapshots = new List<DrawingShapeAddressSnapshot>(sheet.DrawingShapes.Count);
        foreach (var shape in sheet.DrawingShapes)
            snapshots.Add(new DrawingShapeAddressSnapshot(shape, shape.Anchor));

        return snapshots;
    }

    private static IReadOnlyList<PictureAddressSnapshot> CapturePictures(Sheet sheet)
    {
        if (sheet.Pictures.Count == 0)
            return [];

        var snapshots = new List<PictureAddressSnapshot>(sheet.Pictures.Count);
        foreach (var picture in sheet.Pictures)
        {
            snapshots.Add(new PictureAddressSnapshot(
                picture,
                picture.Anchor,
                picture.LinkedSourceRange,
                picture.IsLinkedToSourceRange));
        }

        return snapshots;
    }

    private static IReadOnlyList<SparklineAddressSnapshot> CaptureSparklines(Sheet sheet)
    {
        if (sheet.Sparklines.Count == 0)
            return [];

        var snapshots = new List<SparklineAddressSnapshot>(sheet.Sparklines.Count);
        foreach (var sparkline in sheet.Sparklines)
            snapshots.Add(new SparklineAddressSnapshot(sparkline, sparkline.DataRange, sparkline.Location));

        return snapshots;
    }

    private static IReadOnlyList<FormControlAddressSnapshot> CaptureFormControls(Sheet sheet)
    {
        if (sheet.FormControls.Count == 0)
            return [];

        var snapshots = new List<FormControlAddressSnapshot>(sheet.FormControls.Count);
        foreach (var control in sheet.FormControls)
            snapshots.Add(new FormControlAddressSnapshot(control, control.Anchor, control.LinkedCell, control.ListFillRange));

        return snapshots;
    }

    private static IReadOnlyList<PivotTableAddressSnapshot> CapturePivotTables(Sheet sheet)
    {
        if (sheet.PivotTables.Count == 0)
            return [];

        var snapshots = new List<PivotTableAddressSnapshot>(sheet.PivotTables.Count);
        foreach (var pivotTable in sheet.PivotTables)
            snapshots.Add(new PivotTableAddressSnapshot(pivotTable, pivotTable.SourceRange, pivotTable.TargetRange, pivotTable.LastRenderedRange));

        return snapshots;
    }

    private static IReadOnlyList<PivotCacheSourceSnapshot> CapturePivotCaches(Workbook workbook)
    {
        if (workbook.PivotCaches.Count == 0)
            return [];

        var snapshots = new List<PivotCacheSourceSnapshot>(workbook.PivotCaches.Count);
        foreach (var cache in workbook.PivotCaches)
            snapshots.Add(new PivotCacheSourceSnapshot(cache, cache.SourceSheetName, cache.SourceReference));

        return snapshots;
    }

    private static class EmptyUIntIntDictionary
    {
        internal static readonly IReadOnlyDictionary<uint, int> Instance = new Dictionary<uint, int>();
    }

    internal static void RestoreAddressBearingState(
        Workbook workbook,
        Sheet sheet,
        AddressBearingStateSnapshot? snapshot)
    {
        if (snapshot is null)
            return;

        RestoreStyleOnlyEntries(sheet, snapshot.StyleOnlyEntries);
        RestoreDictionary(sheet.RowOutlineLevels, snapshot.RowOutlineLevels);
        RestoreDictionary(sheet.ColOutlineLevels, snapshot.ColOutlineLevels);
        RestoreSet(sheet.GroupHiddenRows, snapshot.GroupHiddenRows);
        RestoreSet(sheet.GroupHiddenCols, snapshot.GroupHiddenCols);
        RestoreList(sheet.AllowEditRanges, snapshot.AllowEditRanges);
        sheet.PrintTitleRows = snapshot.PrintTitleRows;
        sheet.PrintTitleColumns = snapshot.PrintTitleColumns;
        sheet.RowPageBreaksMetadata = ClonePageBreaksMetadata(snapshot.RowPageBreaksMetadata);
        sheet.ColumnPageBreaksMetadata = ClonePageBreaksMetadata(snapshot.ColumnPageBreaksMetadata);

        workbook.WatchedCells.Clear();
        workbook.WatchedCells.AddRange(snapshot.WatchedCells);
        sheet.CellWatchesMetadata = CloneCellWatchesMetadata(snapshot.CellWatchesMetadata);
        sheet.IgnoredErrorsMetadata = CloneIgnoredErrorsMetadata(snapshot.IgnoredErrorsMetadata);
        sheet.AutoFilter = snapshot.AutoFilter;
        sheet.SmartTags = snapshot.SmartTags;
        sheet.DataConsolidation = snapshot.DataConsolidation;
        sheet.SortState = snapshot.SortState;
        sheet.SingleXmlCells = snapshot.SingleXmlCells;

        sheet.TextBoxes.Clear();
        foreach (var entry in snapshot.TextBoxes)
        {
            entry.TextBox.Anchor = entry.Anchor;
            sheet.TextBoxes.Add(entry.TextBox);
        }

        sheet.DrawingShapes.Clear();
        foreach (var entry in snapshot.DrawingShapes)
        {
            entry.Shape.Anchor = entry.Anchor;
            sheet.DrawingShapes.Add(entry.Shape);
        }

        sheet.Pictures.Clear();
        foreach (var entry in snapshot.Pictures)
        {
            entry.Picture.Anchor = entry.Anchor;
            entry.Picture.LinkedSourceRange = entry.LinkedSourceRange;
            entry.Picture.IsLinkedToSourceRange = entry.IsLinkedToSourceRange;
            sheet.Pictures.Add(entry.Picture);
        }

        sheet.Sparklines.Clear();
        foreach (var entry in snapshot.Sparklines)
        {
            entry.Sparkline.DataRange = entry.DataRange;
            entry.Sparkline.Location = entry.Location;
            sheet.Sparklines.Add(entry.Sparkline);
        }

        sheet.PivotTables.Clear();
        foreach (var entry in snapshot.PivotTables)
        {
            entry.PivotTable.SourceRange = entry.SourceRange;
            entry.PivotTable.TargetRange = entry.TargetRange;
            entry.PivotTable.LastRenderedRange = entry.LastRenderedRange;
            sheet.PivotTables.Add(entry.PivotTable);
        }

        sheet.StructuredTables.Clear();
        sheet.StructuredTables.AddRange(snapshot.StructuredTables);

        foreach (var entry in snapshot.PivotCaches)
            entry.Cache.SourceReference = entry.SourceReference;

        workbook.Scenarios.Clear();
        workbook.Scenarios.AddRange(snapshot.Scenarios);

        sheet.FormControls.Clear();
        foreach (var entry in snapshot.FormControls)
        {
            entry.Control.Anchor        = entry.Anchor;
            entry.Control.LinkedCell    = entry.LinkedCell;
            entry.Control.ListFillRange = entry.ListFillRange;
            sheet.FormControls.Add(entry.Control);
        }
    }

    internal static void ShiftAddressBearingRowsUp(
        Workbook workbook,
        Sheet sheet,
        AddressBearingStateSnapshot snapshot,
        uint start,
        uint count) =>
        ShiftAddressBearingState(workbook, sheet, snapshot, new AddressShift(sheet, AddressShiftAxis.Rows, AddressShiftKind.Insert, start, count));

    internal static void ShiftAddressBearingRowsDown(
        Workbook workbook,
        Sheet sheet,
        AddressBearingStateSnapshot snapshot,
        uint start,
        uint count) =>
        ShiftAddressBearingState(workbook, sheet, snapshot, new AddressShift(sheet, AddressShiftAxis.Rows, AddressShiftKind.Delete, start, count));

    internal static void ShiftAddressBearingColumnsUp(
        Workbook workbook,
        Sheet sheet,
        AddressBearingStateSnapshot snapshot,
        uint start,
        uint count) =>
        ShiftAddressBearingState(workbook, sheet, snapshot, new AddressShift(sheet, AddressShiftAxis.Columns, AddressShiftKind.Insert, start, count));

    internal static void ShiftAddressBearingColumnsDown(
        Workbook workbook,
        Sheet sheet,
        AddressBearingStateSnapshot snapshot,
        uint start,
        uint count) =>
        ShiftAddressBearingState(workbook, sheet, snapshot, new AddressShift(sheet, AddressShiftAxis.Columns, AddressShiftKind.Delete, start, count));

    private static void ShiftAddressBearingState(
        Workbook workbook,
        Sheet sheet,
        AddressBearingStateSnapshot snapshot,
        AddressShift shift)
    {
        ApplyShiftedStyleOnlyEntries(sheet, snapshot.StyleOnlyEntries, shift);
        ShiftOutlineAndGroupCollections(sheet, snapshot, shift);
        RestoreList(sheet.AllowEditRanges, ShiftRanges(snapshot.AllowEditRanges, shift));
        ShiftPrintTitles(sheet, snapshot, shift);
        ShiftPageBreakMetadata(sheet, snapshot, shift);

        ShiftWatchedCells(workbook, snapshot, shift);
        sheet.CellWatchesMetadata = ShiftCellWatchesMetadata(snapshot.CellWatchesMetadata, shift);
        sheet.IgnoredErrorsMetadata = ShiftIgnoredErrorsMetadata(snapshot.IgnoredErrorsMetadata, shift);
        sheet.AutoFilter = ShiftAutoFilter(snapshot.AutoFilter, shift);
        sheet.SmartTags = ShiftSmartTags(snapshot.SmartTags, shift);
        sheet.DataConsolidation = ShiftDataConsolidation(snapshot.DataConsolidation, shift);
        sheet.SortState = ShiftSortState(snapshot.SortState, shift);
        sheet.SingleXmlCells = ShiftSingleXmlCells(snapshot.SingleXmlCells, shift);

        ShiftTextBoxes(sheet, snapshot, shift);
        ShiftDrawingShapes(sheet, snapshot, shift);
        ShiftPictures(sheet, snapshot, shift);
        ShiftSparklines(sheet, snapshot, shift);
        ShiftPivotTables(sheet, snapshot, shift);
        ShiftStructuredTables(sheet, snapshot, shift);
        ShiftPivotCaches(snapshot, shift);
        ShiftScenarios(workbook, snapshot, shift);
        ShiftFormControls(sheet, snapshot, shift);
    }

    private static void ApplyShiftedStyleOnlyEntries(
        Sheet sheet,
        IReadOnlyList<StyleOnlyEntry> entries,
        AddressShift shift)
    {
        ClearStyleOnlyEntries(sheet);
        foreach (var entry in entries)
        {
            if (shift.ShiftCell(entry.Row, entry.Col) is not { } shifted)
                continue;

            sheet.SetStyleOnly(shifted.Row, shifted.Col, entry.StyleId);
        }
    }

    private static void RestoreStyleOnlyEntries(Sheet sheet, IReadOnlyList<StyleOnlyEntry> entries)
    {
        ClearStyleOnlyEntries(sheet);
        foreach (var entry in entries)
            sheet.SetStyleOnly(entry.Row, entry.Col, entry.StyleId);
    }

    private static void ClearStyleOnlyEntries(Sheet sheet)
        => sheet.ClearStyleOnlyEntries();

    private static void ShiftOutlineAndGroupCollections(
        Sheet sheet,
        AddressBearingStateSnapshot snapshot,
        AddressShift shift)
    {
        if (shift.Axis == AddressShiftAxis.Rows)
        {
            RestoreDictionary(sheet.RowOutlineLevels, ShiftDictionaryKeys(snapshot.RowOutlineLevels, shift));
            RestoreSet(sheet.GroupHiddenRows, ShiftIndexes(snapshot.GroupHiddenRows, shift));
        }
        else
        {
            RestoreDictionary(sheet.ColOutlineLevels, ShiftDictionaryKeys(snapshot.ColOutlineLevels, shift));
            RestoreSet(sheet.GroupHiddenCols, ShiftIndexes(snapshot.GroupHiddenCols, shift));
        }
    }

    private static void ShiftPrintTitles(Sheet sheet, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        if (shift.Axis == AddressShiftAxis.Rows)
            sheet.PrintTitleRows = ShiftRepeatRange(snapshot.PrintTitleRows, shift);
        else
            sheet.PrintTitleColumns = ShiftRepeatRange(snapshot.PrintTitleColumns, shift);
    }

    private static void ShiftPageBreakMetadata(Sheet sheet, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        if (shift.Axis == AddressShiftAxis.Rows)
            sheet.RowPageBreaksMetadata = ShiftPageBreaksMetadata(snapshot.RowPageBreaksMetadata, shift);
        else
            sheet.ColumnPageBreaksMetadata = ShiftPageBreaksMetadata(snapshot.ColumnPageBreaksMetadata, shift);
    }

    private static WorksheetRepeatRange? ShiftRepeatRange(WorksheetRepeatRange? range, AddressShift shift)
    {
        if (range is not { } value)
            return null;

        if (shift.Kind == AddressShiftKind.Insert)
        {
            if (value.End < shift.Start)
                return value;

            var start = value.Start >= shift.Start ? value.Start + shift.Count : value.Start;
            return new WorksheetRepeatRange(start, value.End + shift.Count);
        }

        var end = shift.End;
        if (value.End < shift.Start)
            return value;
        if (value.Start > end)
            return new WorksheetRepeatRange(value.Start - shift.Count, value.End - shift.Count);

        var newStart = value.Start < shift.Start ? value.Start : shift.Start;
        var newEnd = value.End > end ? value.End - shift.Count : shift.Start - 1;
        return newEnd >= newStart ? new WorksheetRepeatRange(newStart, newEnd) : null;
    }

    private static List<GridRange> ShiftRanges(IEnumerable<GridRange> ranges, AddressShift shift)
    {
        var shifted = new List<GridRange>();
        foreach (var range in ranges)
        {
            if (shift.ShiftRange(range) is { } shiftedRange)
                shifted.Add(shiftedRange);
        }

        return shifted;
    }

    private static void ShiftWatchedCells(Workbook workbook, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        workbook.WatchedCells.Clear();
        foreach (var address in snapshot.WatchedCells)
        {
            if (shift.ShiftAddress(address) is { } shifted)
                workbook.WatchedCells.Add(shifted);
        }
    }

    private static WorksheetCellWatchesMetadataModel? ShiftCellWatchesMetadata(
        WorksheetCellWatchesMetadataModel? metadata,
        AddressShift shift)
    {
        if (metadata is null)
            return null;

        var clone = new WorksheetCellWatchesMetadataModel
        {
            NativeAttributes = new Dictionary<string, string>(metadata.NativeAttributes, StringComparer.Ordinal)
        };

        foreach (var (reference, attributes) in metadata.WatchNativeAttributes)
        {
            if (ShiftReference(reference, shift) is { } shifted)
                clone.WatchNativeAttributes[shifted] = new Dictionary<string, string>(attributes, StringComparer.Ordinal);
        }

        return clone.NativeAttributes.Count == 0 && clone.WatchNativeAttributes.Count == 0 ? null : clone;
    }

    private static WorksheetIgnoredErrorsMetadataModel? ShiftIgnoredErrorsMetadata(
        WorksheetIgnoredErrorsMetadataModel? metadata,
        AddressShift shift)
    {
        if (metadata is null)
            return null;

        var clone = new WorksheetIgnoredErrorsMetadataModel
        {
            NativeAttributes = new Dictionary<string, string>(metadata.NativeAttributes, StringComparer.Ordinal)
        };

        foreach (var (reference, attributes) in metadata.ErrorNativeAttributes)
        {
            if (ShiftReference(reference, shift) is { } shifted)
                clone.ErrorNativeAttributes[shifted] = new Dictionary<string, string>(attributes, StringComparer.Ordinal);
        }

        return clone.NativeAttributes.Count == 0 && clone.ErrorNativeAttributes.Count == 0 ? null : clone;
    }

    private static WorksheetAutoFilterModel? ShiftAutoFilter(WorksheetAutoFilterModel? autoFilter, AddressShift shift)
    {
        if (autoFilter is null)
            return null;

        var shiftedReference = ShiftReference(autoFilter.Reference, shift);
        if (!string.IsNullOrWhiteSpace(autoFilter.Reference) && shiftedReference is null)
            return null;

        var shiftedColumns = ShiftAutoFilterColumns(autoFilter, shiftedReference, shift).ToList();
        var changed =
            !ReferencesEqual(autoFilter.Reference, shiftedReference) ||
            !AutoFilterColumnsEqual(autoFilter.FilterColumns, shiftedColumns);
        if (!changed)
            return autoFilter;

        var clone = new WorksheetAutoFilterModel(shiftedReference, null)
        {
            NativeAttributes = CloneReadOnlyDictionary(autoFilter.NativeAttributes),
            NativeChildXmls = autoFilter.NativeChildXmls?.ToArray()
        };
        clone.FilterColumns.AddRange(shiftedColumns);
        return clone;
    }

    private static IEnumerable<WorksheetAutoFilterColumnModel> ShiftAutoFilterColumns(
        WorksheetAutoFilterModel autoFilter,
        string? shiftedReference,
        AddressShift shift)
    {
        if (shift.Axis != AddressShiftAxis.Columns ||
            string.IsNullOrWhiteSpace(autoFilter.Reference) ||
            string.IsNullOrWhiteSpace(shiftedReference) ||
            !TryParseSingleReference(autoFilter.Reference, shift, out var oldRange) ||
            !TryParseSingleReference(shiftedReference, shift, out var newRange))
        {
            return autoFilter.FilterColumns.Select(column => CloneAutoFilterColumn(column));
        }

        var shiftedColumns = new List<WorksheetAutoFilterColumnModel>();
        foreach (var column in autoFilter.FilterColumns)
        {
            if (column.ColumnId < 0)
            {
                shiftedColumns.Add(CloneAutoFilterColumn(column));
                continue;
            }

            var absoluteColumn = oldRange.Start.Col + (uint)column.ColumnId;
            if (shift.ShiftIndex(absoluteColumn) is not { } shiftedColumn)
                continue;

            var shiftedColumnId = (long)shiftedColumn - newRange.Start.Col;
            if (shiftedColumnId < 0 || shiftedColumnId >= newRange.ColCount)
                continue;

            shiftedColumns.Add(CloneAutoFilterColumn(column, (int)shiftedColumnId));
        }

        return shiftedColumns;
    }

    private static bool AutoFilterColumnsEqual(
        IReadOnlyList<WorksheetAutoFilterColumnModel> original,
        IReadOnlyList<WorksheetAutoFilterColumnModel> shifted)
    {
        if (original.Count != shifted.Count)
            return false;

        for (var i = 0; i < original.Count; i++)
        {
            if (original[i].ColumnId != shifted[i].ColumnId)
                return false;
        }

        return true;
    }

    private static WorksheetSmartTagsModel? ShiftSmartTags(WorksheetSmartTagsModel? smartTags, AddressShift shift)
    {
        if (smartTags is null)
            return null;

        var changed = false;
        var clone = new WorksheetSmartTagsModel();
        foreach (var cell in smartTags.Cells)
        {
            var shiftedReference = ShiftReference(cell.Reference, shift);
            changed |= !ReferencesEqual(cell.Reference, shiftedReference);
            if (!string.IsNullOrWhiteSpace(cell.Reference) && shiftedReference is null)
                continue;

            clone.Cells.Add(CloneSmartTagCell(cell, shiftedReference));
        }

        if (!changed)
            return smartTags;

        return clone.Cells.Count == 0 ? null : clone;
    }

    private static WorksheetDataConsolidationModel? ShiftDataConsolidation(
        WorksheetDataConsolidationModel? dataConsolidation,
        AddressShift shift)
    {
        if (dataConsolidation is null)
            return null;

        var changed = false;
        var clone = new WorksheetDataConsolidationModel
        {
            Function = dataConsolidation.Function,
            LeftLabels = dataConsolidation.LeftLabels,
            TopLabels = dataConsolidation.TopLabels,
            Link = dataConsolidation.Link,
            NativeAttributes = new Dictionary<string, string>(dataConsolidation.NativeAttributes, StringComparer.Ordinal)
        };

        foreach (var reference in dataConsolidation.References)
        {
            if (!ReferenceSheetMatches(reference.Sheet, shift.SheetName))
            {
                clone.References.Add(CloneDataConsolidationReference(reference, reference.Reference));
                continue;
            }

            var shiftedReference = ShiftReference(reference.Reference, shift);
            changed |= !ReferencesEqual(reference.Reference, shiftedReference);
            if (!string.IsNullOrWhiteSpace(reference.Reference) && shiftedReference is null)
                continue;

            clone.References.Add(CloneDataConsolidationReference(reference, shiftedReference));
        }

        if (!changed)
            return dataConsolidation;

        return clone;
    }

    private static WorksheetSortStateModel? ShiftSortState(WorksheetSortStateModel? sortState, AddressShift shift)
    {
        if (sortState is null)
            return null;

        var shiftedReference = ShiftReference(sortState.Reference, shift);
        if (!string.IsNullOrWhiteSpace(sortState.Reference) && shiftedReference is null)
            return null;

        var changed = !ReferencesEqual(sortState.Reference, shiftedReference);
        var clone = new WorksheetSortStateModel
        {
            Reference = shiftedReference,
            ColumnSort = sortState.ColumnSort,
            CaseSensitive = sortState.CaseSensitive,
            SortMethod = sortState.SortMethod,
            NativeAttributes = new Dictionary<string, string>(sortState.NativeAttributes, StringComparer.Ordinal)
        };

        foreach (var condition in sortState.Conditions)
        {
            var shiftedConditionReference = ShiftReference(condition.Reference, shift);
            changed |= !ReferencesEqual(condition.Reference, shiftedConditionReference);
            if (!string.IsNullOrWhiteSpace(condition.Reference) && shiftedConditionReference is null)
                continue;

            clone.Conditions.Add(CloneSortCondition(condition, shiftedConditionReference));
        }

        return changed ? clone : sortState;
    }

    private static WorksheetSingleXmlCellsModel? ShiftSingleXmlCells(
        WorksheetSingleXmlCellsModel? singleXmlCells,
        AddressShift shift)
    {
        if (singleXmlCells is null)
            return null;

        var changed = false;
        var clone = new WorksheetSingleXmlCellsModel
        {
            NativeAttributes = new Dictionary<string, string>(singleXmlCells.NativeAttributes, StringComparer.Ordinal)
        };

        foreach (var cell in singleXmlCells.Cells)
        {
            var shiftedReference = ShiftReference(cell.Reference, shift);
            changed |= !ReferencesEqual(cell.Reference, shiftedReference);
            if (!string.IsNullOrWhiteSpace(cell.Reference) && shiftedReference is null)
                continue;

            clone.Cells.Add(new WorksheetSingleXmlCellModel
            {
                Id = cell.Id,
                Reference = shiftedReference,
                XmlCellPropertyId = cell.XmlCellPropertyId,
                NativeAttributes = new Dictionary<string, string>(cell.NativeAttributes, StringComparer.Ordinal)
            });
        }

        return changed ? clone : singleXmlCells;
    }

    private static void ShiftTextBoxes(Sheet sheet, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        sheet.TextBoxes.Clear();
        foreach (var entry in snapshot.TextBoxes)
        {
            if (shift.ShiftAddress(entry.Anchor) is not { } anchor)
                continue;

            entry.TextBox.Anchor = anchor;
            sheet.TextBoxes.Add(entry.TextBox);
        }
    }

    private static void ShiftDrawingShapes(Sheet sheet, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        sheet.DrawingShapes.Clear();
        foreach (var entry in snapshot.DrawingShapes)
        {
            if (shift.ShiftAddress(entry.Anchor) is not { } anchor)
                continue;

            entry.Shape.Anchor = anchor;
            sheet.DrawingShapes.Add(entry.Shape);
        }
    }

    private static void ShiftPictures(Sheet sheet, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        sheet.Pictures.Clear();
        foreach (var entry in snapshot.Pictures)
        {
            if (shift.ShiftAddress(entry.Anchor) is not { } anchor)
                continue;

            entry.Picture.Anchor = anchor;
            entry.Picture.LinkedSourceRange = entry.LinkedSourceRange is { } linkedRange
                ? shift.ShiftRange(linkedRange)
                : null;
            entry.Picture.IsLinkedToSourceRange = entry.IsLinkedToSourceRange && entry.Picture.LinkedSourceRange is not null;
            sheet.Pictures.Add(entry.Picture);
        }
    }

    private static void ShiftFormControls(Sheet sheet, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        sheet.FormControls.Clear();
        foreach (var entry in snapshot.FormControls)
        {
            // Shift the anchor GridRange.  When a control has an anchor and the entire anchor
            // falls within the deleted zone, ShiftRange returns null — drop the control (mirrors
            // how TextBoxes / DrawingShapes are handled).
            GridRange? newAnchor;
            if (entry.Anchor is { } anchor)
            {
                newAnchor = shift.ShiftRange(anchor);
                if (newAnchor is null)
                {
                    // Anchor was entirely deleted — remove this control.
                    // Still restore control state so callers don't see stale refs.
                    entry.Control.Anchor        = null;
                    entry.Control.LinkedCell    = null;
                    entry.Control.ListFillRange = null;
                    continue;
                }
            }
            else
            {
                newAnchor = null;
            }

            // Rewrite the LinkedCell and ListFillRange string references via the same
            // ShiftReference path used for pivot-cache source references (handles $A$5, Sheet1!$A$1, etc.).
            var newLinkedCell    = ShiftFormControlRef(entry.LinkedCell, shift);
            var newListFillRange = ShiftFormControlRef(entry.ListFillRange, shift);

            entry.Control.Anchor        = newAnchor;
            entry.Control.LinkedCell    = newLinkedCell;
            entry.Control.ListFillRange = newListFillRange;
            sheet.FormControls.Add(entry.Control);
        }
    }

    private static string? ShiftFormControlRef(string? reference, AddressShift shift)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return reference;

        // Strip leading '=' if present.
        var raw = reference.TrimStart();
        var hasEquals = raw.StartsWith('=');
        if (hasEquals)
            raw = raw[1..].Trim();

        // Reuse the existing ShiftReference path (handles "A1", "$A$5", "Sheet1!$A$1:$A$3", etc.).
        var shifted = ShiftReference(raw, shift);
        if (shifted is null)
            return null; // ref was entirely deleted

        return hasEquals ? "=" + shifted : shifted;
    }

    private static void ShiftSparklines(Sheet sheet, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        sheet.Sparklines.Clear();
        foreach (var entry in snapshot.Sparklines)
        {
            if (shift.ShiftAddress(entry.Location) is not { } location ||
                shift.ShiftRange(entry.DataRange) is not { } dataRange)
            {
                continue;
            }

            entry.Sparkline.Location = location;
            entry.Sparkline.DataRange = dataRange;
            sheet.Sparklines.Add(entry.Sparkline);
        }
    }

    private static void ShiftPivotTables(Sheet sheet, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        sheet.PivotTables.Clear();
        foreach (var entry in snapshot.PivotTables)
        {
            if (shift.ShiftRange(entry.SourceRange) is not { } sourceRange ||
                shift.ShiftRange(entry.TargetRange) is not { } targetRange)
            {
                continue;
            }

            entry.PivotTable.SourceRange = sourceRange;
            entry.PivotTable.TargetRange = targetRange;
            entry.PivotTable.LastRenderedRange = entry.LastRenderedRange is { } lastRenderedRange
                ? shift.ShiftRange(lastRenderedRange)
                : null;
            sheet.PivotTables.Add(entry.PivotTable);
        }
    }

    private static void ShiftStructuredTables(Sheet sheet, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        sheet.StructuredTables.Clear();
        foreach (var table in snapshot.StructuredTables)
        {
            if (shift.ShiftRange(table.Range) is { } range)
                sheet.StructuredTables.Add(CopyStructuredTableWithRange(table, range, shift));
        }
    }

    // A column insert/delete that overlaps the table changes its Range width, but table.Columns
    // (and FilterColumns, keyed by the same 0-based index) describe columns by position within the
    // OLD range. Left untouched, every column after the shift point silently maps onto the wrong
    // physical column (e.g. deleting the table's 3rd column would still leave 'Columns[3]' aligned
    // with what is now physically the table's 4th-turned-3rd column). Reconcile the column list to
    // the new width: drop columns whose position was deleted, and insert placeholder columns at the
    // position(s) newly added to the table by the insert.
    private static List<StructuredTableColumnModel> ReconcileStructuredTableColumns(
        StructuredTableModel table,
        GridRange newRange,
        AddressShift shift)
    {
        if (shift.Axis != AddressShiftAxis.Columns)
            return [.. table.Columns];

        var oldRange = table.Range;
        var usedNames = new HashSet<string>(
            table.Columns.Select(column => column.Name),
            StringComparer.OrdinalIgnoreCase);
        var nextId = 1;
        foreach (var existing in table.Columns)
            nextId = Math.Max(nextId, existing.Id + 1);

        var reconciled = new List<StructuredTableColumnModel>((int)newRange.ColCount);
        for (var col = newRange.Start.Col; col <= newRange.End.Col; col++)
        {
            // Map this new physical column back to where it lived before the shift.
            var oldCol = shift.Kind == AddressShiftKind.Insert
                ? (col >= shift.Start && col <= shift.End ? (uint?)null : (col > shift.End ? col - shift.Count : col))
                : (col >= shift.Start ? col + shift.Count : col);

            if (oldCol is { } sourceCol && sourceCol >= oldRange.Start.Col && sourceCol <= oldRange.End.Col)
            {
                var oldIndex = (int)(sourceCol - oldRange.Start.Col);
                if (oldIndex < table.Columns.Count)
                {
                    reconciled.Add(table.Columns[oldIndex]);
                    continue;
                }
            }

            // Newly-inserted column: Excel auto-names it "ColumnN" (N = 1-based physical position
            // within the table, de-duplicated against every surviving name) and assigns it a fresh id.
            var baseName = $"Column{reconciled.Count + 1}";
            var name = baseName;
            for (var suffix = 2; usedNames.Contains(name); suffix++)
                name = $"{baseName}{suffix.ToString(CultureInfo.InvariantCulture)}";
            usedNames.Add(name);
            reconciled.Add(new StructuredTableColumnModel(nextId++, name));
        }

        return reconciled;
    }

    private static List<StructuredTableFilterColumnModel> ReconcileStructuredTableFilterColumns(
        StructuredTableModel table,
        GridRange newRange,
        AddressShift shift)
    {
        if (shift.Axis != AddressShiftAxis.Columns)
            return [.. table.FilterColumns.Select(column => CloneStructuredTableFilterColumn(column))];

        var oldRange = table.Range;
        var reconciled = new List<StructuredTableFilterColumnModel>();
        foreach (var filterColumn in table.FilterColumns)
        {
            if (filterColumn.ColumnId < 0)
                continue;

            var absoluteColumn = oldRange.Start.Col + (uint)filterColumn.ColumnId;
            if (shift.ShiftIndex(absoluteColumn) is not { } shiftedColumn)
                continue;

            var shiftedColumnId = (long)shiftedColumn - newRange.Start.Col;
            if (shiftedColumnId < 0 || shiftedColumnId >= newRange.ColCount)
                continue;

            reconciled.Add(CloneStructuredTableFilterColumn(filterColumn, (int)shiftedColumnId));
        }

        return reconciled;
    }

    private static void ShiftPivotCaches(AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        foreach (var entry in snapshot.PivotCaches)
        {
            if (!ReferenceSheetMatches(entry.SourceSheetName, shift.SheetName))
            {
                entry.Cache.SourceReference = entry.SourceReference;
                continue;
            }

            entry.Cache.SourceReference = ShiftReference(entry.SourceReference, shift);
        }
    }

    private static void ShiftScenarios(Workbook workbook, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        workbook.Scenarios.Clear();
        foreach (var scenario in snapshot.Scenarios)
        {
            var changedCells = new List<ScenarioCellValue>();
            foreach (var cell in scenario.ChangingCells)
            {
                if (shift.ShiftAddress(cell.Address) is { } address)
                    changedCells.Add(cell with { Address = address });
            }

            if (changedCells.Count > 0)
                workbook.Scenarios.Add(scenario with { ChangingCells = changedCells });
        }
    }

    private static WorksheetPageBreaksMetadataModel? ShiftPageBreaksMetadata(
        WorksheetPageBreaksMetadataModel? metadata,
        AddressShift shift)
    {
        if (metadata is null)
            return null;

        var clone = new WorksheetPageBreaksMetadataModel
        {
            NativeAttributes = new Dictionary<string, string>(metadata.NativeAttributes, StringComparer.Ordinal)
        };
        foreach (var (index, attributes) in metadata.BreakNativeAttributes)
        {
            if (shift.ShiftIndex(index) is { } shiftedIndex)
                clone.BreakNativeAttributes[shiftedIndex] = new Dictionary<string, string>(attributes, StringComparer.Ordinal);
        }

        return clone;
    }

    private static Dictionary<uint, TValue> ShiftDictionaryKeys<TValue>(
        IReadOnlyDictionary<uint, TValue> values,
        AddressShift shift)
    {
        var shifted = new Dictionary<uint, TValue>();
        foreach (var (key, value) in values)
        {
            if (shift.ShiftIndex(key) is { } shiftedKey)
                shifted[shiftedKey] = value;
        }

        return shifted;
    }

    private static HashSet<uint> ShiftIndexes(IEnumerable<uint> values, AddressShift shift)
    {
        var shifted = new HashSet<uint>();
        foreach (var value in values)
        {
            if (shift.ShiftIndex(value) is { } shiftedValue)
                shifted.Add(shiftedValue);
        }

        return shifted;
    }

    private static void RestoreList<T>(List<T> target, IEnumerable<T> snapshot)
    {
        target.Clear();
        target.AddRange(snapshot);
    }

    private static void RestoreDictionary<TKey, TValue>(
        Dictionary<TKey, TValue> target,
        IReadOnlyDictionary<TKey, TValue> snapshot)
        where TKey : notnull
    {
        target.Clear();
        foreach (var (key, value) in snapshot)
            target[key] = value;
    }

    private static void RestoreSet<T>(HashSet<T> target, IEnumerable<T> snapshot)
    {
        target.Clear();
        target.UnionWith(snapshot);
    }

    private static WorksheetAutoFilterColumnModel CloneAutoFilterColumn(
        WorksheetAutoFilterColumnModel column,
        int? columnId = null) =>
        new(
            columnId ?? column.ColumnId,
            column.Values.ToArray(),
            column.IncludeBlank,
            column.CustomFilters.Select(CloneAutoFilterCustomFilter).ToArray(),
            column.CustomFiltersAnd,
            column.CustomFiltersAndRaw,
            CloneReadOnlyDictionary(column.NativeCustomFiltersAttributes),
            CloneAutoFilterTop10(column.Top10),
            CloneAutoFilterDynamicFilter(column.DynamicFilter),
            CloneAutoFilterColorFilter(column.ColorFilter),
            CloneAutoFilterIconFilter(column.IconFilter),
            column.DateGroups.Select(CloneAutoFilterDateGroup).ToArray(),
            CloneReadOnlyDictionary(column.NativeFiltersAttributes),
            column.NativeFilterXmls.ToArray(),
            CloneReadOnlyDictionary(column.NativeAttributes));

    private static WorksheetAutoFilterCustomFilterModel CloneAutoFilterCustomFilter(
        WorksheetAutoFilterCustomFilterModel filter) =>
        new(filter.Operator, filter.Value, CloneReadOnlyDictionary(filter.NativeAttributes));

    private static WorksheetAutoFilterDateGroupItemModel CloneAutoFilterDateGroup(
        WorksheetAutoFilterDateGroupItemModel dateGroup) =>
        dateGroup with { NativeAttributes = CloneReadOnlyDictionary(dateGroup.NativeAttributes) };

    private static WorksheetAutoFilterTop10Model? CloneAutoFilterTop10(WorksheetAutoFilterTop10Model? top10) =>
        top10 is null ? null : top10 with { NativeAttributes = CloneReadOnlyDictionary(top10.NativeAttributes) };

    private static WorksheetAutoFilterDynamicFilterModel? CloneAutoFilterDynamicFilter(
        WorksheetAutoFilterDynamicFilterModel? dynamicFilter) =>
        dynamicFilter is null ? null : dynamicFilter with { NativeAttributes = CloneReadOnlyDictionary(dynamicFilter.NativeAttributes) };

    private static WorksheetAutoFilterColorFilterModel? CloneAutoFilterColorFilter(
        WorksheetAutoFilterColorFilterModel? colorFilter) =>
        colorFilter is null ? null : colorFilter with { NativeAttributes = CloneReadOnlyDictionary(colorFilter.NativeAttributes) };

    private static WorksheetAutoFilterIconFilterModel? CloneAutoFilterIconFilter(
        WorksheetAutoFilterIconFilterModel? iconFilter) =>
        iconFilter is null ? null : iconFilter with { NativeAttributes = CloneReadOnlyDictionary(iconFilter.NativeAttributes) };

    private static WorksheetCellSmartTagsModel CloneSmartTagCell(
        WorksheetCellSmartTagsModel cell,
        string? reference)
    {
        var clone = new WorksheetCellSmartTagsModel
        {
            Reference = reference,
            NativeAttributes = new Dictionary<string, string>(cell.NativeAttributes, StringComparer.Ordinal),
            Tags = cell.Tags.Select(CloneSmartTag).ToList()
        };
        return clone;
    }

    private static WorksheetCellSmartTagModel CloneSmartTag(WorksheetCellSmartTagModel tag) =>
        new()
        {
            Type = tag.Type,
            Deleted = tag.Deleted,
            NativeAttributes = new Dictionary<string, string>(tag.NativeAttributes, StringComparer.Ordinal),
            Properties = tag.Properties.Select(property => new WorksheetCellSmartTagPropertyModel
            {
                Key = property.Key,
                Value = property.Value,
                NativeAttributes = new Dictionary<string, string>(property.NativeAttributes, StringComparer.Ordinal)
            }).ToList()
        };

    private static WorksheetDataConsolidationReferenceModel CloneDataConsolidationReference(
        WorksheetDataConsolidationReferenceModel reference,
        string? shiftedReference) =>
        new()
        {
            Reference = shiftedReference,
            Sheet = reference.Sheet,
            Name = reference.Name,
            NativeAttributes = new Dictionary<string, string>(reference.NativeAttributes, StringComparer.Ordinal)
        };

    private static WorksheetSortConditionModel CloneSortCondition(
        WorksheetSortConditionModel condition,
        string? reference) =>
        new()
        {
            Reference = reference,
            Descending = condition.Descending,
            SortBy = condition.SortBy,
            CustomList = condition.CustomList,
            DxfId = condition.DxfId,
            IconSet = condition.IconSet,
            IconId = condition.IconId,
            NativeAttributes = new Dictionary<string, string>(condition.NativeAttributes, StringComparer.Ordinal)
        };

    private static StructuredTableModel CopyStructuredTableWithRange(
        StructuredTableModel table,
        GridRange range,
        AddressShift shift)
    {
        var clone = new StructuredTableModel
        {
            Id = table.Id,
            Name = table.Name,
            DisplayName = table.DisplayName,
            Range = range,
            HasAutoFilter = table.HasAutoFilter,
            TotalsRowShown = table.TotalsRowShown,
            HeaderRowCount = table.HeaderRowCount,
            TotalsRowCount = table.TotalsRowCount,
            InsertRow = table.InsertRow,
            InsertRowShift = table.InsertRowShift,
            Published = table.Published,
            Comment = table.Comment,
            StyleName = table.StyleName,
            ShowFirstColumn = table.ShowFirstColumn,
            ShowLastColumn = table.ShowLastColumn,
            ShowRowStripes = table.ShowRowStripes,
            ShowColumnStripes = table.ShowColumnStripes,
            PackagePart = table.PackagePart,
            NativeSortStateXml = table.NativeSortStateXml,
            NativeAttributes = CloneReadOnlyDictionary(table.NativeAttributes),
            NativeChildXmls = table.NativeChildXmls?.ToArray(),
            NativeAutoFilterAttributes = CloneReadOnlyDictionary(table.NativeAutoFilterAttributes),
            NativeAutoFilterChildXmls = table.NativeAutoFilterChildXmls?.ToArray(),
            NativeStyleInfoAttributes = CloneReadOnlyDictionary(table.NativeStyleInfoAttributes),
            NativeStyleInfoChildXmls = table.NativeStyleInfoChildXmls?.ToArray()
        };
        clone.Columns.AddRange(ReconcileStructuredTableColumns(table, range, shift));
        clone.FilterColumns.AddRange(ReconcileStructuredTableFilterColumns(table, range, shift));
        return clone;
    }

    private static StructuredTableFilterColumnModel CloneStructuredTableFilterColumn(
        StructuredTableFilterColumnModel column,
        int? columnId = null) =>
        new(
            columnId ?? column.ColumnId,
            column.Values.ToArray(),
            column.IncludeBlank,
            column.CustomFilters.Select(filter => new StructuredTableCustomFilterModel(
                filter.Operator,
                filter.Value,
                CloneReadOnlyDictionary(filter.NativeAttributes))).ToArray(),
            column.CustomFiltersAnd,
            column.CustomFiltersAndRaw,
            CloneReadOnlyDictionary(column.NativeCustomFiltersAttributes),
            column.NativeFilterXmls.ToArray(),
            CloneReadOnlyDictionary(column.NativeAttributes));

    private static WorksheetPageBreaksMetadataModel? ClonePageBreaksMetadata(
        WorksheetPageBreaksMetadataModel? metadata)
    {
        if (metadata is null)
            return null;

        return new WorksheetPageBreaksMetadataModel
        {
            NativeAttributes = new Dictionary<string, string>(metadata.NativeAttributes, StringComparer.Ordinal),
            BreakNativeAttributes = metadata.BreakNativeAttributes.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, string>(pair.Value, StringComparer.Ordinal))
        };
    }

    private static WorksheetCellWatchesMetadataModel? CloneCellWatchesMetadata(
        WorksheetCellWatchesMetadataModel? metadata)
    {
        if (metadata is null)
            return null;

        return new WorksheetCellWatchesMetadataModel
        {
            NativeAttributes = new Dictionary<string, string>(metadata.NativeAttributes, StringComparer.Ordinal),
            WatchNativeAttributes = metadata.WatchNativeAttributes.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, string>(pair.Value, StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static WorksheetIgnoredErrorsMetadataModel? CloneIgnoredErrorsMetadata(
        WorksheetIgnoredErrorsMetadataModel? metadata)
    {
        if (metadata is null)
            return null;

        return new WorksheetIgnoredErrorsMetadataModel
        {
            NativeAttributes = new Dictionary<string, string>(metadata.NativeAttributes, StringComparer.Ordinal),
            ErrorNativeAttributes = metadata.ErrorNativeAttributes.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, string>(pair.Value, StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IReadOnlyDictionary<string, string>? CloneReadOnlyDictionary(
        IReadOnlyDictionary<string, string>? source) =>
        source is null ? null : new Dictionary<string, string>(source, StringComparer.Ordinal);

    private static bool ReferencesEqual(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool ReferenceSheetMatches(string? referenceSheetName, string sheetName) =>
        string.IsNullOrWhiteSpace(referenceSheetName) ||
        string.Equals(referenceSheetName, sheetName, StringComparison.OrdinalIgnoreCase);

    private static string? ShiftReference(string? reference, AddressShift shift)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return reference;

        var tokens = reference.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return reference;

        var shiftedTokens = new List<string>();
        var parsedAny = false;
        foreach (var token in tokens)
        {
            if (!TryShiftReferenceToken(token, shift, out var shiftedToken, out var parsed))
            {
                shiftedTokens.Add(token);
                continue;
            }

            parsedAny |= parsed;
            if (shiftedToken is not null)
                shiftedTokens.Add(shiftedToken);
        }

        if (shiftedTokens.Count == 0 && parsedAny)
            return null;

        return shiftedTokens.Count == tokens.Length
            ? string.Join(' ', shiftedTokens)
            : shiftedTokens.Count == 0 ? null : string.Join(' ', shiftedTokens);
    }

    private static bool TryShiftReferenceToken(
        string token,
        AddressShift shift,
        out string? shiftedToken,
        out bool parsed)
    {
        shiftedToken = null;
        parsed = false;
        if (!TryParseReferenceToken(token, shift, out var reference, out var appliesToSheet))
            return false;

        parsed = true;
        var shiftedRange = shift.ShiftRange(reference.Range);
        if (shiftedRange is null)
            return true;

        shiftedToken = FormatReferenceToken(reference, shiftedRange.Value);
        return true;
    }

    private static bool TryParseSingleReference(string reference, AddressShift shift, out GridRange range)
    {
        range = default;
        var tokens = reference.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length != 1 ||
            !TryParseReferenceToken(tokens[0], shift, out var parsed, out var appliesToSheet) ||
            !appliesToSheet)
        {
            return false;
        }

        range = parsed.Range;
        return true;
    }

    private static bool TryParseReferenceToken(
        string token,
        AddressShift shift,
        out ParsedRangeReference reference,
        out bool appliesToSheet)
    {
        reference = default;
        appliesToSheet = true;
        var localReference = token.Trim();
        var prefix = "";
        var bangIndex = localReference.LastIndexOf('!');
        if (bangIndex >= 0)
        {
            prefix = localReference[..(bangIndex + 1)];
            var sheetName = NormalizeSheetReference(localReference[..bangIndex]);
            if (!string.Equals(sheetName, shift.SheetName, StringComparison.OrdinalIgnoreCase))
            {
                appliesToSheet = false;
                return false;
            }

            localReference = localReference[(bangIndex + 1)..];
        }

        var parts = localReference.Split(':');
        if (parts.Length is not (1 or 2) ||
            !TryParseCellReference(parts[0], shift.SheetId, out var start) ||
            !TryParseCellReference(parts[^1], shift.SheetId, out var end))
        {
            return false;
        }

        reference = new ParsedRangeReference(
            prefix,
            start,
            end,
            new GridRange(start.Address, end.Address),
            parts.Length == 2);
        return true;
    }

    private static bool TryParseCellReference(string value, SheetId sheetId, out ParsedCellReference reference)
    {
        reference = default;
        var span = value.AsSpan().Trim();
        if (span.IsEmpty)
            return false;

        var index = 0;
        var absoluteColumn = index < span.Length && span[index] == '$';
        if (absoluteColumn)
            index++;

        var columnStart = index;
        while (index < span.Length && char.IsAsciiLetter(span[index]))
            index++;
        if (index == columnStart)
            return false;

        var columnName = new string(span[columnStart..index]).ToUpperInvariant();
        var column = CellAddress.ColumnNameToNumber(columnName);
        if (column is < 1 or > CellAddress.MaxCol)
            return false;

        var absoluteRow = index < span.Length && span[index] == '$';
        if (absoluteRow)
            index++;

        var rowStart = index;
        while (index < span.Length && char.IsAsciiDigit(span[index]))
            index++;
        if (index == rowStart || index != span.Length)
            return false;

        if (!uint.TryParse(new string(span[rowStart..index]), out var row) ||
            row is < 1 or > CellAddress.MaxRow)
        {
            return false;
        }

        reference = new ParsedCellReference(new CellAddress(sheetId, row, column), absoluteColumn, absoluteRow);
        return true;
    }

    private static string FormatReferenceToken(ParsedRangeReference reference, GridRange shiftedRange)
    {
        var start = FormatCellReference(reference.Start, shiftedRange.Start);
        if (!reference.WasRange)
            return reference.Prefix + start;

        var end = FormatCellReference(reference.End, shiftedRange.End);
        return $"{reference.Prefix}{start}:{end}";
    }

    private static string FormatCellReference(ParsedCellReference template, CellAddress address) =>
        $"{(template.AbsoluteColumn ? "$" : "")}{CellAddress.NumberToColumnName(address.Col)}{(template.AbsoluteRow ? "$" : "")}{address.Row}";

    private static string NormalizeSheetReference(string reference)
    {
        if (reference.Length >= 2 && reference[0] == '\'' && reference[^1] == '\'')
            return reference[1..^1].Replace("''", "'", StringComparison.Ordinal);

        return reference;
    }

    private readonly record struct AddressShift(
        Sheet Sheet,
        AddressShiftAxis Axis,
        AddressShiftKind Kind,
        uint Start,
        uint Count)
    {
        public SheetId SheetId => Sheet.Id;
        public string SheetName => Sheet.Name;
        public uint End => Start + Count - 1;

        public CellAddress? ShiftCell(uint row, uint col)
        {
            if (Axis == AddressShiftAxis.Rows)
            {
                if (ShiftIndex(row) is not { } shiftedRow)
                    return null;
                return new CellAddress(SheetId, shiftedRow, col);
            }

            if (ShiftIndex(col) is not { } shiftedCol)
                return null;
            return new CellAddress(SheetId, row, shiftedCol);
        }

        public CellAddress? ShiftAddress(CellAddress address)
        {
            if (address.Sheet != SheetId)
                return address;

            return Axis == AddressShiftAxis.Rows
                ? ShiftCell(address.Row, address.Col)
                : ShiftCell(address.Row, address.Col);
        }

        public GridRange? ShiftRange(GridRange range)
        {
            if (range.Start.Sheet != SheetId)
                return range;

            return (Axis, Kind) switch
            {
                (AddressShiftAxis.Rows, AddressShiftKind.Insert) => ShiftRangeRowsUp(range, Start, Count),
                (AddressShiftAxis.Rows, AddressShiftKind.Delete) => ShiftRangeRowsDown(range, Start, Count),
                (AddressShiftAxis.Columns, AddressShiftKind.Insert) => ShiftRangeColumnsUp(range, Start, Count),
                (AddressShiftAxis.Columns, AddressShiftKind.Delete) => ShiftRangeColumnsDown(range, Start, Count),
                _ => range
            };
        }

        public uint? ShiftIndex(uint value)
        {
            if (Kind == AddressShiftKind.Insert)
                return value >= Start ? value + Count : value;

            if (value < Start)
                return value;
            return value > End ? value - Count : null;
        }
    }

    private enum AddressShiftAxis
    {
        Rows,
        Columns
    }

    private enum AddressShiftKind
    {
        Insert,
        Delete
    }

    private readonly record struct ParsedCellReference(
        CellAddress Address,
        bool AbsoluteColumn,
        bool AbsoluteRow);

    private readonly record struct ParsedRangeReference(
        string Prefix,
        ParsedCellReference Start,
        ParsedCellReference End,
        GridRange Range,
        bool WasRange);
}

internal sealed record AddressBearingStateSnapshot(
    IReadOnlyList<StyleOnlyEntry> StyleOnlyEntries,
    IReadOnlyDictionary<uint, int> RowOutlineLevels,
    IReadOnlyDictionary<uint, int> ColOutlineLevels,
    IReadOnlyCollection<uint> GroupHiddenRows,
    IReadOnlyCollection<uint> GroupHiddenCols,
    IReadOnlyList<GridRange> AllowEditRanges,
    WorksheetRepeatRange? PrintTitleRows,
    WorksheetRepeatRange? PrintTitleColumns,
    WorksheetPageBreaksMetadataModel? RowPageBreaksMetadata,
    WorksheetPageBreaksMetadataModel? ColumnPageBreaksMetadata,
    IReadOnlyList<CellAddress> WatchedCells,
    WorksheetCellWatchesMetadataModel? CellWatchesMetadata,
    WorksheetIgnoredErrorsMetadataModel? IgnoredErrorsMetadata,
    WorksheetAutoFilterModel? AutoFilter,
    WorksheetSmartTagsModel? SmartTags,
    WorksheetDataConsolidationModel? DataConsolidation,
    WorksheetSortStateModel? SortState,
    WorksheetSingleXmlCellsModel? SingleXmlCells,
    IReadOnlyList<TextBoxAddressSnapshot> TextBoxes,
    IReadOnlyList<DrawingShapeAddressSnapshot> DrawingShapes,
    IReadOnlyList<PictureAddressSnapshot> Pictures,
    IReadOnlyList<SparklineAddressSnapshot> Sparklines,
    IReadOnlyList<PivotTableAddressSnapshot> PivotTables,
    IReadOnlyList<StructuredTableModel> StructuredTables,
    IReadOnlyList<PivotCacheSourceSnapshot> PivotCaches,
    IReadOnlyList<WorkbookScenario> Scenarios,
    IReadOnlyList<FormControlAddressSnapshot> FormControls);

internal readonly record struct StyleOnlyEntry(uint Row, uint Col, StyleId StyleId);

internal readonly record struct TextBoxAddressSnapshot(TextBoxModel TextBox, CellAddress Anchor);

internal readonly record struct DrawingShapeAddressSnapshot(DrawingShapeModel Shape, CellAddress Anchor);

internal readonly record struct PictureAddressSnapshot(
    PictureModel Picture,
    CellAddress Anchor,
    GridRange? LinkedSourceRange,
    bool IsLinkedToSourceRange);

internal readonly record struct SparklineAddressSnapshot(
    SparklineModel Sparkline,
    GridRange DataRange,
    CellAddress Location);

internal readonly record struct PivotTableAddressSnapshot(
    PivotTableModel PivotTable,
    GridRange SourceRange,
    GridRange TargetRange,
    GridRange? LastRenderedRange);

internal readonly record struct PivotCacheSourceSnapshot(
    PivotCacheModel Cache,
    string? SourceSheetName,
    string? SourceReference);

internal readonly record struct FormControlAddressSnapshot(
    FormControlModel Control,
    GridRange? Anchor,
    string? LinkedCell,
    string? ListFillRange);
