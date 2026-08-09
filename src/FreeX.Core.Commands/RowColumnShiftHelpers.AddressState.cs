using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Formula;
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
            CaptureUIntSet(sheet.CollapsedAnchorRows),
            CaptureUIntSet(sheet.CollapsedAnchorCols),
            CaptureList(sheet.AllowEditRanges),
            CaptureAllowEditRangePasswords(sheet),
            CaptureAllowEditRangeUnlocked(sheet),
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.FrozenRows,
            sheet.FrozenCols,
            sheet.SplitRow,
            sheet.SplitColumn,
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
            CapturePivotTables(workbook),
            CaptureList(sheet.StructuredTables),
            CapturePivotCaches(workbook),
            CaptureList(workbook.Scenarios),
            CaptureFormControls(sheet),
            CaptureCrossSheetFormControlRefs(workbook, sheet),
            CaptureCrossSheetPictureRefs(workbook, sheet));

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

    // M42: AllowEditRangePasswords/UnlockedAllowEditRanges are parallel state to AllowEditRanges,
    // keyed by the exact GridRange stored there (see Sheet.AllowEditRangePasswords doc comment).
    // They must be captured/shifted/restored alongside AllowEditRanges itself so a row/column
    // insert/delete never orphans a range's password (or its per-session unlock) under the old,
    // now-stale GridRange key.
    private static IReadOnlyList<KeyValuePair<GridRange, string?>> CaptureAllowEditRangePasswords(Sheet sheet) =>
        sheet.AllowEditRangePasswords.Count == 0 ? [] : [.. sheet.AllowEditRangePasswords];

    private static IReadOnlyList<GridRange> CaptureAllowEditRangeUnlocked(Sheet sheet) =>
        sheet.UnlockedAllowEditRanges.Count == 0 ? [] : [.. sheet.UnlockedAllowEditRanges];

    private static IReadOnlyList<TextBoxAddressSnapshot> CaptureTextBoxes(Sheet sheet)
    {
        if (sheet.TextBoxes.Count == 0)
            return [];

        var snapshots = new List<TextBoxAddressSnapshot>(sheet.TextBoxes.Count);
        foreach (var textBox in sheet.TextBoxes)
            snapshots.Add(new TextBoxAddressSnapshot(textBox, textBox.Anchor, textBox.Width, textBox.Height));

        return snapshots;
    }

    private static IReadOnlyList<DrawingShapeAddressSnapshot> CaptureDrawingShapes(Sheet sheet)
    {
        if (sheet.DrawingShapes.Count == 0)
            return [];

        var snapshots = new List<DrawingShapeAddressSnapshot>(sheet.DrawingShapes.Count);
        foreach (var shape in sheet.DrawingShapes)
            snapshots.Add(new DrawingShapeAddressSnapshot(shape, shape.Anchor, shape.Width, shape.Height));

        return snapshots;
    }

    private static IReadOnlyList<PictureAddressSnapshot> CapturePictures(Sheet sheet)
    {
        if (sheet.Pictures.Count == 0)
            return [];

        var snapshots = new List<PictureAddressSnapshot>(sheet.Pictures.Count);
        foreach (var picture in sheet.Pictures)
        {
            // P23: a same-sheet linked picture's rendered grid geometry (SourceRowCount/
            // SourceColumnCount) and cached cell snapshot (Cells) get rewritten in place by
            // RefreshLinkedPictureSnapshot whenever a structural edit lands inside its
            // LinkedSourceRange (see ShiftPictures below). Snapshot them here too — alongside
            // the range/anchor fields already captured — so RestoreAddressBearingState can put
            // the picture's whole rendered state back on undo, not just its addressing.
            snapshots.Add(new PictureAddressSnapshot(
                picture,
                picture.Anchor,
                picture.LinkedSourceRange,
                picture.IsLinkedToSourceRange,
                picture.SourceRowCount,
                picture.SourceColumnCount,
                [.. picture.Cells],
                picture.Width,
                picture.Height));
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
            snapshots.Add(new FormControlAddressSnapshot(control, control.Anchor, control.AnchorOffsets, control.LinkedCell, control.ListFillRange));

        return snapshots;
    }

    // P83: a form control's LinkedCell/ListFillRange can explicitly point at a *different* sheet
    // than the one hosting the control (e.g. a checkbox on Sheet2 with LinkedCell "Sheet1!$A$5" —
    // cross-sheet linked cells are supported, see FormControlInteractionService.TryResolveLinkedCell).
    // CaptureFormControls/ShiftFormControls above only ever see the single sheet being structurally
    // edited, so a control hosted elsewhere that references the edited sheet never gets its string
    // ref rewritten. Capture those workbook-wide (excluding the edited sheet, which the per-sheet
    // pass above already owns) so ShiftCrossSheetFormControlRefs can fix them up too.
    private static IReadOnlyList<CrossSheetFormControlRefSnapshot> CaptureCrossSheetFormControlRefs(
        Workbook workbook,
        Sheet editedSheet)
    {
        List<CrossSheetFormControlRefSnapshot>? snapshots = null;
        foreach (var hostSheet in workbook.Sheets)
        {
            if (ReferenceEquals(hostSheet, editedSheet) || hostSheet.FormControls.Count == 0)
                continue;

            foreach (var control in hostSheet.FormControls)
            {
                if (!ReferencesSheetByName(control.LinkedCell, editedSheet.Name) &&
                    !ReferencesSheetByName(control.ListFillRange, editedSheet.Name))
                {
                    continue;
                }

                snapshots ??= [];
                snapshots.Add(new CrossSheetFormControlRefSnapshot(control, control.LinkedCell, control.ListFillRange));
            }
        }

        return snapshots ?? (IReadOnlyList<CrossSheetFormControlRefSnapshot>)[];
    }

    // R14-camera-linked-picture-1: a linked picture's LinkedSourceRange (Paste Special > Linked
    // Picture — our "camera" tool) is a GridRange whose Start.Sheet can point at a *different*
    // sheet than the one hosting the picture (e.g. a picture on Sheet1 linked to Sheet2!A1:B3).
    // CapturePictures/ShiftPictures above only ever see the single sheet being structurally
    // edited, so a picture hosted elsewhere whose source range references the edited sheet never
    // gets its LinkedSourceRange shifted or its cached Cells/geometry refreshed — it keeps drawing
    // the pre-edit cells at the wrong coordinates. Capture those workbook-wide (excluding the
    // edited sheet, which the per-sheet pass above already owns) so ShiftCrossSheetPictureRefs can
    // fix them up too, mirroring CaptureCrossSheetFormControlRefs for form controls.
    private static IReadOnlyList<CrossSheetPictureRefSnapshot> CaptureCrossSheetPictureRefs(
        Workbook workbook,
        Sheet editedSheet)
    {
        List<CrossSheetPictureRefSnapshot>? snapshots = null;
        foreach (var hostSheet in workbook.Sheets)
        {
            if (ReferenceEquals(hostSheet, editedSheet) || hostSheet.Pictures.Count == 0)
                continue;

            foreach (var picture in hostSheet.Pictures)
            {
                if (picture.LinkedSourceRange is not { } sourceRange || sourceRange.Start.Sheet != editedSheet.Id)
                    continue;

                snapshots ??= [];
                snapshots.Add(new CrossSheetPictureRefSnapshot(
                    picture,
                    sourceRange,
                    picture.IsLinkedToSourceRange,
                    picture.SourceRowCount,
                    picture.SourceColumnCount,
                    [.. picture.Cells]));
            }
        }

        return snapshots ?? (IReadOnlyList<CrossSheetPictureRefSnapshot>)[];
    }

    /// <summary>True when <paramref name="reference"/> contains an explicit "SheetName!" (or
    /// 'Quoted Name'!) qualifier equal to <paramref name="sheetName"/>. Bare/unqualified refs
    /// belong to the control's own hosting sheet, not the edited sheet, so they must return false
    /// here — only an explicit cross-sheet qualifier makes a foreign-hosted control's ref subject
    /// to this sheet's structural edits.</summary>
    private static bool ReferencesSheetByName(string? reference, string sheetName)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        var raw = reference.TrimStart();
        if (raw.StartsWith('=')) raw = raw[1..].Trim();

        foreach (var token in raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bangIndex = token.LastIndexOf('!');
            if (bangIndex < 0)
                continue;

            var tokenSheetName = NormalizeSheetReference(token[..bangIndex]);
            if (string.Equals(tokenSheetName, sheetName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // N33: a pivot table's SourceRange can reference a *different* sheet than the one it is placed
    // on (e.g. a pivot built from Sheet1!A1:D100 but placed on Sheet2, Excel's default "New
    // Worksheet" destination). Capturing/shifting only the edited sheet's own PivotTables would miss
    // exactly that common case — mirrors the workbook-wide walk already used for charts
    // (CaptureChartDataRanges/ShiftChartRowsUp in RowColumnShiftHelpers.PrintAndCharts.cs) so a pivot
    // hosted anywhere in the workbook still has its SourceRange/TargetRange corrected when the sheet
    // either range points at is structurally edited.
    private static IReadOnlyList<PivotTableAddressSnapshot> CapturePivotTables(Workbook workbook)
    {
        List<PivotTableAddressSnapshot>? snapshots = null;
        foreach (var hostSheet in workbook.Sheets)
        {
            if (hostSheet.PivotTables.Count == 0)
                continue;

            snapshots ??= new List<PivotTableAddressSnapshot>();
            foreach (var pivotTable in hostSheet.PivotTables)
                snapshots.Add(new PivotTableAddressSnapshot(
                    pivotTable, pivotTable.SourceRange, pivotTable.TargetRange, pivotTable.LastRenderedRange, hostSheet.Id));
        }

        return snapshots ?? (IReadOnlyList<PivotTableAddressSnapshot>)[];
    }

    private static IReadOnlyList<PivotCacheSourceSnapshot> CapturePivotCaches(Workbook workbook)
    {
        if (workbook.PivotCaches.Count == 0)
            return [];

        var snapshots = new List<PivotCacheSourceSnapshot>(workbook.PivotCaches.Count);
        foreach (var cache in workbook.PivotCaches)
            snapshots.Add(new PivotCacheSourceSnapshot(cache, cache.SourceSheetName, cache.SourceReference, cache.SourceTableId));

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
        RestoreSet(sheet.CollapsedAnchorRows, snapshot.CollapsedAnchorRows);
        RestoreSet(sheet.CollapsedAnchorCols, snapshot.CollapsedAnchorCols);
        RestoreList(sheet.AllowEditRanges, snapshot.AllowEditRanges);
        RestoreAllowEditRangePasswords(sheet, snapshot.AllowEditRangePasswords);
        RestoreAllowEditRangeUnlocked(sheet, snapshot.UnlockedAllowEditRanges);
        sheet.PrintTitleRows = snapshot.PrintTitleRows;
        sheet.PrintTitleColumns = snapshot.PrintTitleColumns;
        sheet.FrozenRows = snapshot.FrozenRows;
        sheet.FrozenCols = snapshot.FrozenCols;
        sheet.SplitRow = snapshot.SplitRow;
        sheet.SplitColumn = snapshot.SplitColumn;
        sheet.RowPageBreaksMetadata = ClonePageBreaksMetadata(snapshot.RowPageBreaksMetadata);
        sheet.ColumnPageBreaksMetadata = ClonePageBreaksMetadata(snapshot.ColumnPageBreaksMetadata);

        RestoreWatchedCells(workbook, snapshot);
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
            // R86-commands-insert-move-refadjust-5-2: undo a "move and size with cells" resize
            // (see ResizeSpanForShift) back to the exact pre-edit size, not just the pre-edit anchor.
            entry.TextBox.Width = entry.Width;
            entry.TextBox.Height = entry.Height;
            sheet.TextBoxes.Add(entry.TextBox);
        }

        sheet.DrawingShapes.Clear();
        foreach (var entry in snapshot.DrawingShapes)
        {
            entry.Shape.Anchor = entry.Anchor;
            entry.Shape.Width = entry.Width;
            entry.Shape.Height = entry.Height;
            sheet.DrawingShapes.Add(entry.Shape);
        }

        sheet.Pictures.Clear();
        foreach (var entry in snapshot.Pictures)
        {
            entry.Picture.Anchor = entry.Anchor;
            entry.Picture.LinkedSourceRange = entry.LinkedSourceRange;
            entry.Picture.IsLinkedToSourceRange = entry.IsLinkedToSourceRange;
            entry.Picture.Width = entry.Width;
            entry.Picture.Height = entry.Height;

            // P23: undo a structural edit that had refreshed a linked picture's rendered
            // snapshot (RefreshLinkedPictureSnapshot) must also put the geometry/cell cache back,
            // not just the range/anchor — otherwise the picture keeps drawing the post-edit grid
            // even though its LinkedSourceRange above was correctly restored to the pre-edit range.
            entry.Picture.SourceRowCount = entry.SourceRowCount;
            entry.Picture.SourceColumnCount = entry.SourceColumnCount;
            entry.Picture.Cells.Clear();
            entry.Picture.Cells.AddRange(entry.Cells);

            sheet.Pictures.Add(entry.Picture);
        }

        sheet.Sparklines.Clear();
        foreach (var entry in snapshot.Sparklines)
        {
            entry.Sparkline.DataRange = entry.DataRange;
            entry.Sparkline.Location = entry.Location;
            sheet.Sparklines.Add(entry.Sparkline);
        }

        // N33: PivotTables is a workbook-wide snapshot (a pivot's SourceRange can point at a
        // different sheet than the one it is placed on) — clear every host sheet that appears in
        // the snapshot, not just the sheet being edited, mirroring RestoreChartDataRanges.
        foreach (var hostSheetId in DistinctPivotHostSheets(snapshot.PivotTables))
            workbook.GetSheet(hostSheetId)?.PivotTables.Clear();

        foreach (var entry in snapshot.PivotTables)
        {
            entry.PivotTable.SourceRange = entry.SourceRange;
            entry.PivotTable.TargetRange = entry.TargetRange;
            entry.PivotTable.LastRenderedRange = entry.LastRenderedRange;
            workbook.GetSheet(entry.HostSheet)?.PivotTables.Add(entry.PivotTable);
        }

        sheet.StructuredTables.Clear();
        sheet.StructuredTables.AddRange(snapshot.StructuredTables);

        foreach (var entry in snapshot.PivotCaches)
        {
            entry.Cache.SourceReference = entry.SourceReference;
            // R107-round2: undoes ShiftStructuredTables's orphan-id pin (if any) the same way every
            // other field on this snapshot is put back — see PivotCacheSourceSnapshot.SourceTableId.
            entry.Cache.SourceTableId = entry.SourceTableId;
        }

        workbook.Scenarios.Clear();
        workbook.Scenarios.AddRange(snapshot.Scenarios);

        sheet.FormControls.Clear();
        foreach (var entry in snapshot.FormControls)
        {
            entry.Control.Anchor        = entry.Anchor;
            entry.Control.AnchorOffsets = entry.AnchorOffsets;
            entry.Control.LinkedCell    = entry.LinkedCell;
            entry.Control.ListFillRange = entry.ListFillRange;
            sheet.FormControls.Add(entry.Control);
        }

        // P83: restore string refs on controls hosted on OTHER sheets that point at this sheet.
        // These controls are never removed from their own sheet's FormControls list (only the
        // edited sheet's own controls are cleared/re-added above), so just put the ref strings
        // back in place — no Clear/Add dance needed.
        foreach (var entry in snapshot.CrossSheetFormControlRefs)
        {
            entry.Control.LinkedCell    = entry.LinkedCell;
            entry.Control.ListFillRange = entry.ListFillRange;
        }

        // R14-camera-linked-picture-1: restore LinkedSourceRange (+ the geometry/cell cache a
        // structural edit may have refreshed) on pictures hosted on OTHER sheets whose source
        // range points at this sheet. These pictures are never removed from their own sheet's
        // Pictures list (only the edited sheet's own pictures are cleared/re-added above), so just
        // put the range/cache fields back in place — no Clear/Add dance needed, mirroring
        // CrossSheetFormControlRefs above.
        foreach (var entry in snapshot.CrossSheetPictureRefs)
        {
            entry.Picture.LinkedSourceRange = entry.LinkedSourceRange;
            entry.Picture.IsLinkedToSourceRange = entry.IsLinkedToSourceRange;
            entry.Picture.SourceRowCount = entry.SourceRowCount;
            entry.Picture.SourceColumnCount = entry.SourceColumnCount;
            entry.Picture.Cells.Clear();
            entry.Picture.Cells.AddRange(entry.Cells);
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
        ShiftAllowEditRangePasswords(sheet, snapshot.AllowEditRangePasswords, shift);
        ShiftAllowEditRangeUnlocked(sheet, snapshot.UnlockedAllowEditRanges, shift);
        ShiftPrintTitles(sheet, snapshot, shift);
        ShiftFreezeAndSplitPanes(sheet, snapshot, shift);
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
        ShiftPictures(workbook, sheet, snapshot, shift);
        ShiftCrossSheetPictureRefs(workbook, snapshot, shift);
        ShiftSparklines(sheet, snapshot, shift);
        ShiftPivotTables(workbook, snapshot, shift);
        ShiftStructuredTables(workbook, sheet, snapshot, shift);
        ShiftPivotCaches(snapshot, shift);
        ShiftScenarios(workbook, snapshot, shift);
        ShiftFormControls(sheet, snapshot, shift);
        ShiftCrossSheetFormControlRefs(snapshot, shift);
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
            RestoreSet(sheet.CollapsedAnchorRows, ShiftIndexes(snapshot.CollapsedAnchorRows, shift));
            ExtendOutlineGroupIntoInsertedIndexes(sheet.RowOutlineLevels, sheet.GroupHiddenRows, shift);
        }
        else
        {
            RestoreDictionary(sheet.ColOutlineLevels, ShiftDictionaryKeys(snapshot.ColOutlineLevels, shift));
            RestoreSet(sheet.GroupHiddenCols, ShiftIndexes(snapshot.GroupHiddenCols, shift));
            RestoreSet(sheet.CollapsedAnchorCols, ShiftIndexes(snapshot.CollapsedAnchorCols, shift));
            ExtendOutlineGroupIntoInsertedIndexes(sheet.ColOutlineLevels, sheet.GroupHiddenCols, shift);
        }
    }

    // R114-outline-group-insert-extend-1: Excel's outline groups are purely positional -- a
    // contiguous run of rows/columns sharing outlineLevel >= 1 IS the group (see
    // RowOutlineGroupScope.Resolve in GroupRowsCommand.cs, which walks exactly such runs). The
    // plain per-key remap above (ShiftDictionaryKeys/ShiftIndexes, using AddressShift.ShiftIndex)
    // only relocates *existing* level entries; it never assigns a level to a newly-inserted
    // row/column, so inserting in the middle of a group (e.g. Insert Sheet Rows at row 5 inside a
    // 3-8 group) left the new row at implicit level 0 and split one collapsible band into two.
    // Real Excel instead extends the enclosing run to cover the inserted rows/columns. Detect that
    // case here (Insert only -- Delete never creates a new index that needs a level) by checking
    // whether the index immediately above the insertion point (untouched by the shift, since it is
    // < shift.Start) and the index immediately below it (the first pre-existing index pushed past
    // the inserted block, landing at shift.Start + shift.Count) already carry the SAME nonzero
    // level -- i.e. the insertion point fell strictly inside one contiguous run. If so, stamp that
    // level onto every newly-inserted index too (and, if the enclosing run is currently collapsed
    // on both sides, hide the new indexes as well so the whole extended band still collapses as one
    // unit instead of leaving a visible gap).
    private static void ExtendOutlineGroupIntoInsertedIndexes(
        Dictionary<uint, int> levels,
        HashSet<uint> hiddenIndexes,
        AddressShift shift)
    {
        if (shift.Kind != AddressShiftKind.Insert || shift.Start == 0)
            return;

        var above = shift.Start - 1;
        var below = shift.Start + shift.Count;

        if (!levels.TryGetValue(above, out var aboveLevel) || aboveLevel <= 0)
            return;
        if (!levels.TryGetValue(below, out var belowLevel) || belowLevel != aboveLevel)
            return;

        for (var i = shift.Start; i < below; i++)
            levels[i] = aboveLevel;

        if (hiddenIndexes.Contains(above) && hiddenIndexes.Contains(below))
        {
            for (var i = shift.Start; i < below; i++)
                hiddenIndexes.Add(i);
        }
    }

    private static void ShiftPrintTitles(Sheet sheet, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        if (shift.Axis == AddressShiftAxis.Rows)
            sheet.PrintTitleRows = ShiftRepeatRange(snapshot.PrintTitleRows, shift);
        else
            sheet.PrintTitleColumns = ShiftRepeatRange(snapshot.PrintTitleColumns, shift);
    }

    // R111-commands-freeze-split-shift-2: Freeze Panes (Sheet.FrozenRows/FrozenCols) and Split Panes
    // (Sheet.SplitRow/SplitColumn) are the structurally identical sibling of Print Titles above (both
    // "pin a boundary at a row/column position") but were never re-anchored on insert/delete, so
    // inserting a title row above a frozen header left the freeze band one row short of the header
    // (or shrinking it deleted a row inside the band without shrinking the frozen count) — the split
    // boundary must move in lockstep with the shift exactly like Print Titles' WorksheetRepeatRange.
    // Reads from the live sheet fields (not the snapshot) because, unlike every other piece of state
    // here, nothing else in this pipeline mutates FrozenRows/FrozenCols/SplitRow/SplitColumn before
    // this call runs — the snapshot only exists so RestoreAddressBearingState can undo this shift.
    private static void ShiftFreezeAndSplitPanes(Sheet sheet, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        if (shift.Axis == AddressShiftAxis.Rows)
        {
            sheet.FrozenRows = ShiftFrozenBandCount(snapshot.FrozenRows, shift);
            if (snapshot.SplitRow is { } splitRow)
                sheet.SplitRow = shift.ShiftIndex(splitRow);
        }
        else
        {
            sheet.FrozenCols = ShiftFrozenBandCount(snapshot.FrozenCols, shift);
            if (snapshot.SplitColumn is { } splitColumn)
                sheet.SplitColumn = shift.ShiftIndex(splitColumn);
        }
    }

    // Frozen row/column bands are always anchored at index 1 (Excel only ever freezes "the top N
    // rows" / "the left N columns"), so the band is modeled here as the synthetic repeat range
    // [1, frozenCount] and pushed through the exact same ShiftRepeatRange math Print Titles uses.
    // ShiftRepeatRange's returned Start is discarded — the band's start never moves, only its
    // End (the frozen count) does — which is exactly right for both directions:
    //  * Insert at/above the band (shift.Start <= frozenCount): End grows by shift.Count, so the
    //    newly-inserted rows/columns join the frozen band and the pinned content stays pinned.
    //  * Insert below the band: ShiftRepeatRange's value.End < shift.Start guard leaves it untouched.
    //  * Delete entirely inside the band: End shrinks by shift.Count.
    //  * Delete straddling or engulfing the band: End collapses to shift.Start - 1 (clamped to 0 by
    //    the null fallback below), matching ShiftRepeatRange's own overlap branch.
    private static uint ShiftFrozenBandCount(uint frozenCount, AddressShift shift)
    {
        if (frozenCount == 0)
            return 0;

        return ShiftRepeatRange(new WorksheetRepeatRange(1, frozenCount), shift)?.End ?? 0;
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

    // M42: rekeys Sheet.AllowEditRangePasswords onto the post-shift GridRange so a range's own
    // password survives a row/column insert/delete alongside AllowEditRanges itself. A range that
    // the shift deletes entirely (ShiftRange returns null) drops its password too, matching
    // AllowEditRanges dropping the range itself.
    private static void ShiftAllowEditRangePasswords(
        Sheet sheet,
        IReadOnlyList<KeyValuePair<GridRange, string?>> passwords,
        AddressShift shift)
    {
        sheet.AllowEditRangePasswords.Clear();
        foreach (var (range, password) in passwords)
        {
            if (shift.ShiftRange(range) is { } shiftedRange)
                sheet.AllowEditRangePasswords[shiftedRange] = password;
        }
    }

    private static void RestoreAllowEditRangePasswords(
        Sheet sheet,
        IReadOnlyList<KeyValuePair<GridRange, string?>> passwords)
    {
        sheet.AllowEditRangePasswords.Clear();
        foreach (var (range, password) in passwords)
            sheet.AllowEditRangePasswords[range] = password;
    }

    // M42: rekeys Sheet.UnlockedAllowEditRanges (the in-memory, per-session "already entered the
    // correct range password" gate) onto the post-shift GridRange so an already-unlocked range does
    // not spuriously re-prompt for its password merely because a row/column shift moved it, while a
    // range the shift deletes entirely is dropped.
    private static void ShiftAllowEditRangeUnlocked(
        Sheet sheet,
        IReadOnlyList<GridRange> unlocked,
        AddressShift shift)
    {
        sheet.UnlockedAllowEditRanges.Clear();
        foreach (var range in unlocked)
        {
            if (shift.ShiftRange(range) is { } shiftedRange)
                sheet.UnlockedAllowEditRanges.Add(shiftedRange);
        }
    }

    private static void RestoreAllowEditRangeUnlocked(Sheet sheet, IReadOnlyList<GridRange> unlocked)
    {
        sheet.UnlockedAllowEditRanges.Clear();
        foreach (var range in unlocked)
            sheet.UnlockedAllowEditRanges.Add(range);
    }

    private static void ShiftWatchedCells(Workbook workbook, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        // Shifted is null when the structural edit itself deleted this watch's row/column — that
        // watch was never present in the live list post-shift, so on undo it must always come back
        // (the delete's own removal is exactly what undo is reversing) rather than being subject to
        // "did the user touch this" reconciliation below.
        var pairs = new List<(CellAddress Original, CellAddress? Shifted)>(snapshot.WatchedCells.Count);
        var shiftedCells = new List<CellAddress>(snapshot.WatchedCells.Count);
        foreach (var address in snapshot.WatchedCells)
        {
            var shifted = shift.ShiftAddress(address);
            pairs.Add((address, shifted));
            if (shifted is { } survived)
                shiftedCells.Add(survived);
        }

        workbook.WatchedCells.Clear();
        workbook.WatchedCells.AddRange(shiftedCells);

        // Remember the exact original->shifted mapping the shift produced so a later undo
        // (RestoreAddressBearingState) can tell the shift's own effect apart from any Watch Window
        // add/remove the user performed on workbook.WatchedCells after this command ran but before
        // it was undone.
        snapshot.PostShiftWatchedCells = pairs;
    }

    /// <summary>
    /// Restores <see cref="Workbook.WatchedCells"/> from <paramref name="snapshot"/> on undo, but
    /// reconciles against any Watch Window add/remove the user made directly on the live list after
    /// the row/column command ran (WatchWindowService.AddWatch/RemoveWatch are not IWorkbookCommands
    /// and so never appear on the undo stack). If this snapshot was never run through a structural
    /// shift (e.g. undoing a command whose Apply never reached ShiftAddressBearingState), there is
    /// nothing to reconcile against and we fall back to the previous unconditional restore.
    /// </summary>
    private static void RestoreWatchedCells(Workbook workbook, AddressBearingStateSnapshot snapshot)
    {
        if (snapshot.PostShiftWatchedCells is not { } pairs)
        {
            workbook.WatchedCells.Clear();
            workbook.WatchedCells.AddRange(snapshot.WatchedCells);
            return;
        }

        // Consume the live list against the shifted side of each original->shifted pair, one live
        // occurrence per pair, so duplicate addresses are handled correctly.
        var unmatchedLive = new List<CellAddress>(workbook.WatchedCells);
        var restored = new List<CellAddress>(snapshot.WatchedCells.Count);
        foreach (var (original, shifted) in pairs)
        {
            if (shifted is not { } survived)
            {
                // The structural edit itself deleted this watch (its row/column was removed) — undo
                // always brings it back, regardless of any unrelated Watch Window activity.
                restored.Add(original);
                continue;
            }

            var index = unmatchedLive.IndexOf(survived);
            if (index < 0)
                continue; // user removed this watch after the command ran; don't resurrect it.

            unmatchedLive.RemoveAt(index);
            restored.Add(original);
        }

        // Whatever remains in unmatchedLive was added by the user after the command ran (it was never
        // produced by the shift) — keep it, unshifted, after undo instead of silently discarding it.
        restored.AddRange(unmatchedLive);

        workbook.WatchedCells.Clear();
        workbook.WatchedCells.AddRange(restored);
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
            NativeXml = ShiftSortStateNativeXml(sortState.NativeXml, shift),
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

    // Rewrites the `ref` attribute on a raw <sortState>/<sortCondition> XML payload (captured verbatim
    // on load, e.g. via XlsxWorksheetSortStateMapper.Read or the structured-table sortState reader) so a
    // structural row/column insert or delete keeps the sort range in sync WITHOUT discarding any
    // full-fidelity content the payload carries (in particular <extLst> Excel-2010+ extension blocks,
    // which have no other representation in the model and would otherwise be permanently lost the first
    // time a shift forces the from-scratch element builder to run instead of round-tripping this string).
    private static string? ShiftSortStateNativeXml(string? nativeXml, AddressShift shift)
    {
        if (string.IsNullOrWhiteSpace(nativeXml))
            return nativeXml;

        XElement element;
        try
        {
            element = XElement.Parse(nativeXml);
        }
        catch
        {
            // Malformed native payload from an older save; leave it untouched rather than lose it.
            return nativeXml;
        }

        var refAttribute = element.Attribute("ref");
        if (refAttribute is not null)
        {
            var shiftedRef = ShiftReference(refAttribute.Value, shift);
            if (shiftedRef is null)
                refAttribute.Remove();
            else
                refAttribute.Value = shiftedRef;
        }

        var sortConditionName = XName.Get("sortCondition", element.Name.NamespaceName);
        foreach (var condition in element.Elements(sortConditionName).ToList())
        {
            var conditionRefAttribute = condition.Attribute("ref");
            if (conditionRefAttribute is null)
                continue;

            var shiftedConditionRef = ShiftReference(conditionRefAttribute.Value, shift);
            if (shiftedConditionRef is null)
                condition.Remove();
            else
                conditionRefAttribute.Value = shiftedConditionRef;
        }

        return element.ToString(SaveOptions.DisableFormatting);
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
            if (!TryShiftAnchoredDrawingObject(shift, entry.TextBox.DrawingAnchorKind, entry.Anchor, out var anchor))
                continue;

            entry.TextBox.Anchor = anchor;
            // R86-commands-insert-move-refadjust-5-2 / R127-editas-shift-gate: see ResizeSpanForShift.
            if (entry.TextBox.DrawingAnchorKind == ChartDrawingAnchorKind.TwoCell)
            {
                if (shift.Axis == AddressShiftAxis.Rows)
                    entry.TextBox.Height = ResizeSpanForShift(sheet, entry.Anchor, entry.Height, shift);
                else
                    entry.TextBox.Width = ResizeSpanForShift(sheet, entry.Anchor, entry.Width, shift);
            }
            sheet.TextBoxes.Add(entry.TextBox);
        }
    }

    private static void ShiftDrawingShapes(Sheet sheet, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        sheet.DrawingShapes.Clear();
        foreach (var entry in snapshot.DrawingShapes)
        {
            if (!TryShiftAnchoredDrawingObject(shift, entry.Shape.DrawingAnchorKind, entry.Anchor, out var anchor))
                continue;

            entry.Shape.Anchor = anchor;
            // R86-commands-insert-move-refadjust-5-2 / R127-editas-shift-gate: see ResizeSpanForShift.
            if (entry.Shape.DrawingAnchorKind == ChartDrawingAnchorKind.TwoCell)
            {
                if (shift.Axis == AddressShiftAxis.Rows)
                    entry.Shape.Height = ResizeSpanForShift(sheet, entry.Anchor, entry.Height, shift);
                else
                    entry.Shape.Width = ResizeSpanForShift(sheet, entry.Anchor, entry.Width, shift);
            }
            sheet.DrawingShapes.Add(entry.Shape);
        }
    }

    private static void ShiftPictures(Workbook workbook, Sheet sheet, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        sheet.Pictures.Clear();
        foreach (var entry in snapshot.Pictures)
        {
            if (!TryShiftAnchoredDrawingObject(shift, entry.Picture.DrawingAnchorKind, entry.Anchor, out var anchor))
                continue;

            entry.Picture.Anchor = anchor;
            // R86-commands-insert-move-refadjust-5-2 / R127-editas-shift-gate: see ResizeSpanForShift.
            if (entry.Picture.DrawingAnchorKind == ChartDrawingAnchorKind.TwoCell)
            {
                if (shift.Axis == AddressShiftAxis.Rows)
                    entry.Picture.Height = ResizeSpanForShift(sheet, entry.Anchor, entry.Height, shift);
                else
                    entry.Picture.Width = ResizeSpanForShift(sheet, entry.Anchor, entry.Width, shift);
            }
            var shiftedRange = entry.LinkedSourceRange is { } linkedRange
                ? shift.ShiftRange(linkedRange)
                : null;
            entry.Picture.LinkedSourceRange = shiftedRange;
            entry.Picture.IsLinkedToSourceRange = entry.IsLinkedToSourceRange && shiftedRange is not null;

            // A row/column insert or delete that lands inside a same-sheet linked picture's source
            // range grows/shrinks/moves LinkedSourceRange above, but the rendered grid geometry
            // (SourceRowCount/SourceColumnCount) and cached cell content (Cells) are otherwise never
            // touched. Left stale, the picture keeps drawing its old snapshot at the old dimensions
            // even though the linked range now covers different cells. Excel refreshes a linked
            // picture's content on structural edits to its source range, so re-snapshot here.
            if (entry.Picture.IsLinkedToSourceRange &&
                shiftedRange is { } newRange &&
                entry.LinkedSourceRange is { } oldRange &&
                newRange != oldRange)
            {
                RefreshLinkedPictureSnapshot(workbook, sheet, entry.Picture, newRange);
            }

            sheet.Pictures.Add(entry.Picture);
        }
    }

    /// <summary>
    /// R127-editas-shift-gate: gates a picture/shape/text box's row/column insert-delete MOVE on its
    /// captured <c>DrawingAnchorKind</c>, exactly like <see cref="ShiftChartPositionRowsUp"/> (in
    /// RowColumnShiftHelpers.PrintAndCharts.cs) already gates a chart's move on
    /// <c>chart.DrawingAnchorKind != ChartDrawingAnchorKind.Absolute</c>. An <c>absoluteAnchor</c>
    /// ("don't move or size with cells") source is pinned to the sheet's pixel grid, not to any cell --
    /// per <c>XlsxDrawingAnchorApplier.ApplyToPicture</c>'s own doc comment its loaded <c>Anchor</c> is
    /// always the sheet origin (row 1/col 1) with the real position living in AnchorOffsetX/Y, so
    /// calling <see cref="AddressShift.ShiftAddress"/> on it would wrongly relocate it whenever the
    /// shift's start is at/before row-or-col 1 (i.e. almost any insert). <c>oneCellAnchor</c> ("move but
    /// don't size") and <c>twoCellAnchor</c> ("move and size") both still move with
    /// <see cref="AddressShift.ShiftAddress"/> like Excel's own move-with-cells behavior -- only the
    /// RESIZE (handled separately by the <c>DrawingAnchorKind == TwoCell</c> gate at each call site) is
    /// exclusive to twoCellAnchor.
    /// </summary>
    private static bool TryShiftAnchoredDrawingObject(
        AddressShift shift, ChartDrawingAnchorKind kind, CellAddress currentAnchor, out CellAddress shiftedAnchor)
    {
        if (kind == ChartDrawingAnchorKind.Absolute)
        {
            shiftedAnchor = currentAnchor;
            return true;
        }

        if (shift.ShiftAddress(currentAnchor) is { } anchor)
        {
            shiftedAnchor = anchor;
            return true;
        }

        shiftedAnchor = default;
        return false;
    }

    /// <summary>
    /// R86-commands-insert-move-refadjust-5-2: Excel's default "Move and size with cells"
    /// (twoCellAnchor) placement not only moves an anchored object's Anchor cell (already handled by
    /// <see cref="AddressShift.ShiftAddress"/> above) but also grows/shrinks its Height (row axis) or
    /// Width (column axis) when the insert/delete band falls INSIDE the object's existing pixel span
    /// — strictly after its <paramref name="anchor"/> cell but before its far edge — so the object's
    /// far edge keeps tracking the same underlying row/column it originally ended on, instead of
    /// silently ending up covering fewer/more rows or columns than it originally spanned. An anchor
    /// at/after the shift boundary already moves as a whole via ShiftAddress and needs no resize; a
    /// shift entirely past the object's far edge doesn't touch it either.
    ///
    /// The delta uses the sheet's DEFAULT row/column size rather than precisely summing the
    /// (possibly custom-sized) rows/columns inside the shifted band itself — a deliberate
    /// simplification that keeps this resize order-independent with respect to the
    /// RowHeights/ColumnWidths dictionary re-keying elsewhere in the same Apply() call (that
    /// re-keying only ever touches rows/columns at or after the shift's own start, which never
    /// includes anything before <paramref name="anchor"/> or before the shift's start — the two
    /// positions this method actually measures), at the cost of being approximate when the shifted
    /// band itself contains custom-sized rows/columns.
    /// </summary>
    private static double ResizeSpanForShift(Sheet sheet, CellAddress anchor, double currentSize, AddressShift shift)
    {
        if (shift.Axis == AddressShiftAxis.Rows)
        {
            if (shift.Start <= anchor.Row) return currentSize; // anchor itself moves wholesale
            var anchorTop = CumulativeRowTop(sheet, anchor.Row);
            var anchorBottom = anchorTop + currentSize;
            var shiftTop = CumulativeRowTop(sheet, shift.Start);
            if (shiftTop >= anchorBottom) return currentSize; // shift entirely past the far edge

            var delta = shift.Count * sheet.DefaultRowHeight;
            return shift.Kind == AddressShiftKind.Insert
                ? currentSize + delta
                : Math.Max(1.0, currentSize - Math.Min(delta, anchorBottom - shiftTop));
        }
        else
        {
            if (shift.Start <= anchor.Col) return currentSize;
            var anchorLeft = CumulativeColumnLeft(sheet, anchor.Col);
            var anchorRight = anchorLeft + currentSize;
            var shiftLeft = CumulativeColumnLeft(sheet, shift.Start);
            if (shiftLeft >= anchorRight) return currentSize;

            var delta = shift.Count * sheet.DefaultColumnWidth * 8;
            return shift.Kind == AddressShiftKind.Insert
                ? currentSize + delta
                : Math.Max(1.0, currentSize - Math.Min(delta, anchorRight - shiftLeft));
        }
    }

    /// <summary>
    /// Rebuilds a linked picture's rendered grid dimensions and cached cell snapshot from the live
    /// contents of its (possibly resized/relocated) linked source range. Only same-sheet ranges reach
    /// here, since <see cref="AddressShift.ShiftRange"/> leaves cross-sheet ranges untouched.
    /// </summary>
    private static void RefreshLinkedPictureSnapshot(Workbook workbook, Sheet sheet, PictureModel picture, GridRange sourceRange)
    {
        picture.SourceRowCount = sourceRange.RowCount;
        picture.SourceColumnCount = sourceRange.ColCount;

        picture.Cells.Clear();
        for (var row = sourceRange.Start.Row; row <= sourceRange.End.Row; row++)
        {
            for (var col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
            {
                var cell = sheet.GetCell(row, col);
                var styleId = cell?.StyleId
                    ?? sheet.GetStyleOnly(row, col)
                    ?? StyleId.Default;
                var style = workbook.GetStyle(styleId);
                var value = cell?.Value ?? BlankValue.Instance;

                picture.Cells.Add(new PictureCellSnapshot(
                    row - sourceRange.Start.Row,
                    col - sourceRange.Start.Col,
                    FormatPictureCellText(value, style.NumberFormat, workbook.Uses1904DateSystem),
                    style.Clone(),
                    value is NumberValue or DateTimeValue));
            }
        }
    }

    /// <summary>
    /// Renders a linked picture's cell text using the source cell's own number format, so a linked
    /// picture keeps showing the formatted value (e.g. "$1,234.50") on every refresh, exactly as it
    /// did at the moment it was pasted (Excel camera parity; see R14-camera-linked-picture-2). A raw
    /// <c>ToString(CultureInfo.CurrentCulture)</c> would silently strip currency/percent/date/custom
    /// formats after the first structural edit to the source range.
    /// </summary>
    private static string FormatPictureCellText(ScalarValue value, string numberFormat, bool uses1904DateSystem) =>
        FreeX.Core.Formula.NumberFormatter.Format(value, numberFormat, uses1904DateSystem);

    // R14-camera-linked-picture-1: shifts/refreshes pictures hosted on OTHER sheets whose
    // LinkedSourceRange points at the sheet being structurally edited (mirrors
    // ShiftCrossSheetFormControlRefs for form controls). The picture's own Anchor never moves —
    // only its Anchor's *hosting* sheet's edits would move that, and this entry's picture lives on
    // a different sheet — so only LinkedSourceRange (and, when it moved, the refreshed
    // geometry/cell cache) are touched here.
    private static void ShiftCrossSheetPictureRefs(Workbook workbook, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        foreach (var entry in snapshot.CrossSheetPictureRefs)
        {
            var shiftedRange = shift.ShiftRange(entry.LinkedSourceRange);
            entry.Picture.LinkedSourceRange = shiftedRange;
            entry.Picture.IsLinkedToSourceRange = entry.IsLinkedToSourceRange && shiftedRange is not null;

            if (entry.Picture.IsLinkedToSourceRange &&
                shiftedRange is { } newRange &&
                newRange != entry.LinkedSourceRange)
            {
                var sourceSheet = workbook.GetSheet(newRange.Start.Sheet);
                if (sourceSheet is not null)
                    RefreshLinkedPictureSnapshot(workbook, sourceSheet, entry.Picture, newRange);
            }
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
            var removed = false;
            if (entry.Anchor is { } anchor)
            {
                newAnchor = shift.ShiftRange(anchor);
                if (newAnchor is null)
                    removed = true;
            }
            else
            {
                newAnchor = null;
            }

            // R14-form-controls-1: the sub-cell EMU AnchorOffsets (preferred over the whole-cell
            // Anchor by FormControlRenderPlanner.TryCreateAnchorRange/HasSubCellOffsets whenever
            // present — the normal case for XLSX-loaded controls) must shift in lockstep with
            // Anchor, or the control keeps rendering/hit-testing at its pre-shift position even
            // though its logical Anchor cell moved. A VML-only control (Anchor null, AnchorOffsets
            // set) has no whole-cell anchor to drive removal, so ShiftAnchorOffsets' own deletion
            // signal is what removes it when its row/column is deleted.
            var newAnchorOffsets = ShiftAnchorOffsets(entry.AnchorOffsets, shift, out var offsetsDeleted);
            if (!removed && entry.Anchor is null && offsetsDeleted)
                removed = true;

            if (removed)
            {
                // Anchor (or, for a VML-only control, AnchorOffsets) was entirely deleted — remove
                // this control. Still restore control state so callers don't see stale refs.
                entry.Control.Anchor        = null;
                entry.Control.AnchorOffsets = null;
                entry.Control.LinkedCell    = null;
                entry.Control.ListFillRange = null;
                continue;
            }

            // Rewrite the LinkedCell and ListFillRange string references via the same
            // ShiftReference path used for pivot-cache source references (handles $A$5, Sheet1!$A$1, etc.).
            var newLinkedCell    = ShiftFormControlRef(entry.LinkedCell, shift);
            var newListFillRange = ShiftFormControlRef(entry.ListFillRange, shift);

            entry.Control.Anchor        = newAnchor;
            entry.Control.AnchorOffsets = newAnchorOffsets;
            entry.Control.LinkedCell    = newLinkedCell;
            entry.Control.ListFillRange = newListFillRange;
            sheet.FormControls.Add(entry.Control);
        }
    }

    /// <summary>
    /// Shifts a form control's sub-cell <see cref="DrawingAnchorRange"/> the same way the whole-cell
    /// <see cref="GridRange"/> Anchor is shifted: bridges the 0-based offset row/col to the 1-based
    /// <see cref="CellAddress"/> row/col <see cref="AddressShift.ShiftRange"/> expects (mirroring
    /// FormControlRenderPlanner.TryCreateAnchorRange's whole-cell fallback conversion), applies the
    /// shift, and converts back — preserving each point's EMU sub-cell offset, which never changes.
    /// Sets <paramref name="deleted"/> when the offsets' row/column span was entirely removed by the
    /// shift (mirrors ShiftRange returning null for a fully-deleted GridRange).
    /// </summary>
    private static DrawingAnchorRange? ShiftAnchorOffsets(DrawingAnchorRange? offsets, AddressShift shift, out bool deleted)
    {
        deleted = false;
        if (offsets is null)
            return null;

        var range = new GridRange(
            new CellAddress(shift.SheetId, offsets.From.Row + 1, offsets.From.Column + 1),
            new CellAddress(shift.SheetId, offsets.To.Row + 1, offsets.To.Column + 1));

        if (shift.ShiftRange(range) is not { } shiftedRange)
        {
            deleted = true;
            return null;
        }

        return new DrawingAnchorRange(
            new DrawingAnchorPoint(shiftedRange.Start.Col - 1, offsets.From.ColumnOffsetEmu, shiftedRange.Start.Row - 1, offsets.From.RowOffsetEmu),
            new DrawingAnchorPoint(shiftedRange.End.Col - 1, offsets.To.ColumnOffsetEmu, shiftedRange.End.Row - 1, offsets.To.RowOffsetEmu));
    }

    // P83: rewrite LinkedCell/ListFillRange on controls hosted on OTHER sheets that explicitly
    // qualify a reference to the edited sheet (e.g. Sheet2's checkbox with LinkedCell
    // "Sheet1!$A$5" when Sheet1 is the one being structurally edited). ShiftFormControlRef only
    // rewrites tokens whose sheet-qualifier matches shift.SheetName and leaves everything else
    // (including bare/unqualified tokens, which belong to the control's own hosting sheet) byte-
    // for-byte unchanged, so it is safe to call unconditionally for every captured entry here —
    // no anchor/removal handling is needed since these controls are never hosted on the edited
    // sheet in the first place.
    //
    // R17-form-controls-linkedcell-2: allowBareToken:false is required here — a bare/unqualified
    // token (e.g. "$B$1") always means "on the control's OWN hosting sheet" in Excel, never on
    // whatever sheet happens to be structurally edited. Every entry reaching this method lives on
    // a sheet OTHER than shift.SheetId (see comment above), so a bare token must never be parsed
    // against shift.SheetId/shifted — only an explicit "Sheet1!..." qualifier that matches
    // shift.SheetName may be rewritten.
    private static void ShiftCrossSheetFormControlRefs(AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        foreach (var entry in snapshot.CrossSheetFormControlRefs)
        {
            entry.Control.LinkedCell    = ShiftFormControlRef(entry.LinkedCell, shift, allowBareToken: false);
            entry.Control.ListFillRange = ShiftFormControlRef(entry.ListFillRange, shift, allowBareToken: false);
        }
    }

    private static string? ShiftFormControlRef(string? reference, AddressShift shift, bool allowBareToken = true)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return reference;

        // Strip leading '=' if present.
        var raw = reference.TrimStart();
        var hasEquals = raw.StartsWith('=');
        if (hasEquals)
            raw = raw[1..].Trim();

        // Reuse the existing ShiftReference path (handles "A1", "$A$5", "Sheet1!$A$1:$A$3", etc.).
        // allowBareToken:false (used for cross-sheet controls) suppresses shifting an unqualified
        // token, which always belongs to the control's own hosting sheet, not shift.SheetId.
        var shifted = ShiftReference(raw, shift, allowBareToken);
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

    // N33: workbook-wide (see CapturePivotTables) so a pivot table's SourceRange is corrected even
    // when the pivot itself is hosted on a different sheet than the one being structurally edited.
    // AddressShift.ShiftRange already no-ops a range on a sheet other than the one being shifted
    // (range.Start.Sheet != SheetId), so it is safe to call for SourceRange/TargetRange/
    // LastRenderedRange regardless of which of those actually lives on the edited sheet.
    private static void ShiftPivotTables(Workbook workbook, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        foreach (var hostSheetId in DistinctPivotHostSheets(snapshot.PivotTables))
            workbook.GetSheet(hostSheetId)?.PivotTables.Clear();

        foreach (var entry in snapshot.PivotTables)
        {
            var hostSheet = workbook.GetSheet(entry.HostSheet);
            if (hostSheet is null)
                continue;

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

            RemapPivotFieldSourceIndexes(entry.PivotTable, entry.SourceRange, sourceRange, shift);

            hostSheet.PivotTables.Add(entry.PivotTable);
        }
    }

    // R116-commands-pivot-column-shift-fieldindex: PivotFieldModel/PivotDataFieldModel.SourceFieldIndex
    // (plus PivotSortModel.FieldIndex, PivotLabelFilterModel/PivotValueFilterModel.SourceFieldIndex, and
    // PivotCalculatedItemModel.SourceFieldIndex) are raw ordinals naming one of the pivot's live source
    // columns, captured once when each binding was created. Every refresh re-derives `headers`/`rows`/
    // `cache.Fields` fresh from the CURRENT physical column layout (ReadHeaders/ReadSourceRows/
    // ReconcileCacheFields in PivotTableRefreshService), so when an ordinary column insert/delete lands
    // strictly inside a pivot's SourceRange, ShiftRangeColumnsUp/Down leaves Start.Col alone and pushes
    // every later column left/right -- the SourceRange silently grows/shrinks to keep covering the same
    // fields, but every existing SourceFieldIndex-based binding stays numerically unchanged and now
    // silently names a DIFFERENT physical column. It stays in-bounds (so IsValidField/IsValidDataField's
    // bounds-only pruning never catches it), so a completely ordinary "insert a column in the middle of my
    // data" edit followed by a refresh/slicer-click silently mis-binds row/column grouping, data-field
    // aggregation, sort, and label/value filters to the wrong column with no error. Real Excel's pivot
    // field bindings survive a mid-range column insert/delete unchanged (an insert only ever appends a new
    // field to the END of the field list; existing fields keep referring to the same source column) --
    // mirror that here by remapping every source-column-based binding through the SAME AddressShift that
    // just moved the pivot's SourceRange, so each binding keeps pointing at the identical physical column
    // it named before the edit. A column that was itself deleted has no valid destination -- ShiftIndex
    // returns null for it -- so the binding is set to the existing -1 "invalid" sentinel, which
    // IsValidField/IsValidDataField already prune on the very next refresh, exactly like a field whose
    // backing column vanished any other way (R92-app-pivot-drilldown-5-3).
    private static void RemapPivotFieldSourceIndexes(
        PivotTableModel pivotTable,
        GridRange oldSourceRange,
        GridRange newSourceRange,
        AddressShift shift)
    {
        // Only a column-axis shift can move which physical column a SourceFieldIndex names. A row
        // shift never changes column identity, and a whole-row SourceRange (spans every column
        // already) is left untouched by ShiftRangeColumnsUp/Down itself (see their own
        // IsWholeRowSelection guard), so there is nothing to remap for it either.
        if (shift.Axis != AddressShiftAxis.Columns ||
            oldSourceRange.Start.Sheet != shift.SheetId ||
            SelectionRangeService.IsWholeRowSelection(oldSourceRange))
        {
            return;
        }

        int Remap(int sourceFieldIndex)
        {
            // Already invalid (e.g. a calculated data field's -1 placeholder, or a stale binding
            // from an earlier edit) -- nothing meaningful to shift.
            if (sourceFieldIndex < 0)
                return sourceFieldIndex;

            var absoluteCol = oldSourceRange.Start.Col + (uint)sourceFieldIndex;
            if (shift.ShiftIndex(absoluteCol) is not { } shiftedAbsoluteCol)
                return -1;

            return (int)(shiftedAbsoluteCol - newSourceRange.Start.Col);
        }

        for (var index = 0; index < pivotTable.RowFields.Count; index++)
        {
            var field = pivotTable.RowFields[index];
            pivotTable.RowFields[index] = field with { SourceFieldIndex = Remap(field.SourceFieldIndex) };
        }

        for (var index = 0; index < pivotTable.ColumnFields.Count; index++)
        {
            var field = pivotTable.ColumnFields[index];
            pivotTable.ColumnFields[index] = field with { SourceFieldIndex = Remap(field.SourceFieldIndex) };
        }

        for (var index = 0; index < pivotTable.PageFields.Count; index++)
        {
            var field = pivotTable.PageFields[index];
            pivotTable.PageFields[index] = field with { SourceFieldIndex = Remap(field.SourceFieldIndex) };
        }

        for (var index = 0; index < pivotTable.DataFields.Count; index++)
        {
            var field = pivotTable.DataFields[index];
            pivotTable.DataFields[index] = field with { SourceFieldIndex = Remap(field.SourceFieldIndex) };
        }

        for (var index = 0; index < pivotTable.CalculatedItems.Count; index++)
        {
            var item = pivotTable.CalculatedItems[index];
            pivotTable.CalculatedItems[index] = item with { SourceFieldIndex = Remap(item.SourceFieldIndex) };
        }

        for (var index = 0; index < pivotTable.LabelFilters.Count; index++)
        {
            var filter = pivotTable.LabelFilters[index];
            pivotTable.LabelFilters[index] = filter with { SourceFieldIndex = Remap(filter.SourceFieldIndex) };
        }

        for (var index = 0; index < pivotTable.ValueFilters.Count; index++)
        {
            var filter = pivotTable.ValueFilters[index];
            if (filter.SourceFieldIndex is { } filterSourceFieldIndex)
                pivotTable.ValueFilters[index] = filter with { SourceFieldIndex = Remap(filterSourceFieldIndex) };
        }

        for (var index = 0; index < pivotTable.Sorts.Count; index++)
        {
            var sort = pivotTable.Sorts[index];
            pivotTable.Sorts[index] = sort with { FieldIndex = Remap(sort.FieldIndex) };
        }
    }

    private static IEnumerable<SheetId> DistinctPivotHostSheets(IReadOnlyList<PivotTableAddressSnapshot> pivotTables)
    {
        if (pivotTables.Count == 0)
            yield break;

        var seen = new HashSet<SheetId>();
        foreach (var entry in pivotTables)
        {
            if (seen.Add(entry.HostSheet))
                yield return entry.HostSheet;
        }
    }

    // R110-formula-structuredref-rowcoldelete-ref: find the structured tables on `sheet` whose
    // ENTIRE range would be consumed by a row delete of [startRow, startRow+count-1] -- i.e. the
    // same "nothing survives" condition ShiftStructuredTables below detects via
    // AddressShift.ShiftRange(table.Range) returning null. Computed independently (over the
    // live, not-yet-mutated sheet.StructuredTables) so DeleteRowsCommand.Apply can feed the
    // result into every FormulaRewriter pass (cell formulas, named formulas, CF/DV rules, chart
    // verbatim formulas) as DeleteRowsOp.DeletedTableNames -- mirroring how DeleteSheetOp already
    // carries its own DeletedTableNames to convert dangling Table[...] structured references to
    // #REF! instead of leaving them to evaluate as #NAME? once StructuredReferenceResolver can no
    // longer find the table. Must be called BEFORE ShiftStructuredTables/ShiftAddressBearingState
    // runs (which clears and rebuilds sheet.StructuredTables from the shifted snapshot).
    internal static List<string> FindStructuredTablesRemovedByRowDelete(Sheet sheet, uint startRow, uint count)
    {
        List<string>? removed = null;
        foreach (var table in sheet.StructuredTables)
        {
            if (ShiftRangeRowsDown(table.Range, startRow, count) is null)
                (removed ??= []).Add(table.Name);
        }
        return removed ?? [];
    }

    // Column-delete counterpart of FindStructuredTablesRemovedByRowDelete above.
    internal static List<string> FindStructuredTablesRemovedByColumnDelete(Sheet sheet, uint startCol, uint count)
    {
        List<string>? removed = null;
        foreach (var table in sheet.StructuredTables)
        {
            if (ShiftRangeColumnsDown(table.Range, startCol, count) is null)
                (removed ??= []).Add(table.Name);
        }
        return removed ?? [];
    }

    // R115-formula-structuredref-coldelete-survivingtable-ref: counterpart of
    // FindStructuredTablesRemovedByColumnDelete above for the OTHER outcome of a column delete -- a
    // table that SURVIVES the delete (only some of its columns fall inside the deleted band) still
    // loses those columns' names (ReconcileStructuredTableColumns below rebuilds Columns to the
    // post-delete width), so any Table[ColumnName] structured reference elsewhere in the workbook
    // naming one of them is now exactly as dead as a reference to a fully-deleted table. Computed
    // independently over the live, not-yet-mutated sheet.StructuredTables (same timing requirement
    // as FindStructuredTablesRemovedByColumnDelete: must run BEFORE ShiftStructuredTables clears and
    // rebuilds sheet.StructuredTables) so DeleteColumnsCommand.Apply can feed the result into every
    // FormulaRewriter pass as DeleteColsOp.DeletedColumnNamesByTable. A fully-consumed table (whole
    // range inside the deleted band) is excluded here -- it's covered by DeletedTableNames instead,
    // and reporting its columns as merely "removed" would be redundant (and its own Columns/Range are
    // about to be discarded, not reconciled).
    internal static Dictionary<string, IReadOnlyList<string>> FindStructuredTableColumnsRemovedByColumnDelete(
        Sheet sheet, uint startCol, uint count)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var endCol = startCol + count - 1;

        foreach (var table in sheet.StructuredTables)
        {
            if (ShiftRangeColumnsDown(table.Range, startCol, count) is null)
                continue; // fully consumed -- handled via DeletedTableNames, not here.

            List<string>? removedNames = null;
            for (var col = table.Range.Start.Col; col <= table.Range.End.Col; col++)
            {
                if (col < startCol || col > endCol)
                    continue;

                var index = (int)(col - table.Range.Start.Col);
                if (index < table.Columns.Count)
                    (removedNames ??= []).Add(table.Columns[index].Name);
            }

            if (removedNames is { Count: > 0 })
                result[table.Name] = removedNames;
        }

        return result;
    }

    private static void ShiftStructuredTables(Workbook workbook, Sheet sheet, AddressBearingStateSnapshot snapshot, AddressShift shift)
    {
        sheet.StructuredTables.Clear();
        foreach (var table in snapshot.StructuredTables)
        {
            if (shift.ShiftRange(table.Range) is { } range)
            {
                sheet.StructuredTables.Add(CopyStructuredTableWithRange(table, range, shift));
                continue;
            }

            // R107-round2: a row/column delete that fully consumes a structured table's range is a
            // THIRD way (alongside Convert to Range and Delete Sheet) a table's name gets freed
            // workbook-wide -- shift.ShiftRange returning null here means the table itself is gone,
            // not merely resized. Pin any never-refreshed table-backed pivot cache's SourceTableId the
            // same way those two sites do (see CommandGuards.PinOrphanedPivotCacheSourceTableIds), so
            // a later rename/create onto the freed name can't get silently rebound to by the next
            // refresh's null-id fallback. Undo is handled by RestoreAddressBearingState restoring
            // every cache's SourceTableId from the snapshot taken before this shift ran (see
            // CapturePivotCaches/PivotCacheSourceSnapshot) -- no separate unpin list needed here.
            CommandGuards.PinOrphanedPivotCacheSourceTableIds(workbook, table);
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

        // The column shift itself (not just row position) can also relocate the cell references
        // INSIDE a surviving column's own CalculatedColumnFormula/TotalsRowFormula anchor text --
        // e.g. inserting a column before "Price" leaves a Total column's calculated-column formula
        // "A2*B2" referencing the now-blank inserted column instead of the real (shifted) Price
        // column. Rewrite that anchor text with the same column op RewriteAllFormulas already
        // applies to ordinary live sheet-cell formulas, so the metadata stays in lockstep.
        RewriteOperation columnFormulaOp = shift.Kind == AddressShiftKind.Insert
            ? new InsertColsOp(shift.SheetName, shift.Start, shift.Count)
            : new DeleteColsOp(shift.SheetName, shift.Start, shift.Count);

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
                    reconciled.Add(RewriteSurvivingTableColumnFormulas(
                        table.Columns[oldIndex], columnFormulaOp, shift.SheetName));
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

    // Rewrites a surviving column's anchored CalculatedColumnFormula/TotalsRowFormula text for the
    // column insert/delete that just happened, mirroring RewriteAllFormulas' treatment of ordinary
    // live sheet-cell formulas (RowColumnShiftHelpers.Formulas.cs). Rewrite returns null when the
    // formula has no reference to the shifted band, in which case the original text is kept as-is.
    private static StructuredTableColumnModel RewriteSurvivingTableColumnFormulas(
        StructuredTableColumnModel column, RewriteOperation op, string hostSheetName)
    {
        var calculatedColumnFormula = RewriteTableColumnFormula(column.CalculatedColumnFormula, op, hostSheetName);
        var totalsRowFormula = RewriteTableColumnFormula(column.TotalsRowFormula, op, hostSheetName);
        if (ReferenceEquals(calculatedColumnFormula, column.CalculatedColumnFormula) &&
            ReferenceEquals(totalsRowFormula, column.TotalsRowFormula))
        {
            return column;
        }

        return column with
        {
            CalculatedColumnFormula = calculatedColumnFormula,
            TotalsRowFormula = totalsRowFormula
        };
    }

    private static string? RewriteTableColumnFormula(string? formulaText, RewriteOperation op, string hostSheetName) =>
        string.IsNullOrWhiteSpace(formulaText) ? formulaText : FormulaRewriter.Rewrite(formulaText, op, hostSheetName) ?? formulaText;

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
        // A whole-row DELETE that consumes exactly the table's old Totals row shrinks table.Range so
        // the physical totals row no longer exists -- real Excel automatically unchecks "Total Row"
        // in that case. Copying TotalsRowShown verbatim would leave it true and mislabel the table's
        // new (genuine, untouched) last data row as the totals row for every downstream data-body/
        // totals-bounds computation (GetDataBodyRowBounds, DataBodyRange/IsDataBodyRow).
        var totalsRowShown = table.TotalsRowShown
            && !(shift.Axis == AddressShiftAxis.Rows
                && shift.Kind == AddressShiftKind.Delete
                && table.Range.End.Row >= shift.Start
                && table.Range.End.Row <= shift.End);

        var clone = new StructuredTableModel
        {
            Id = table.Id,
            Name = table.Name,
            DisplayName = table.DisplayName,
            Range = range,
            HasAutoFilter = table.HasAutoFilter,
            TotalsRowShown = totalsRowShown,
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
            NativeSortStateXml = ShiftSortStateNativeXml(table.NativeSortStateXml, shift),
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

    private static string? ShiftReference(string? reference, AddressShift shift, bool allowBareToken = true)
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
            if (!TryShiftReferenceToken(token, shift, out var shiftedToken, out var parsed, allowBareToken))
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
        out bool parsed,
        bool allowBareToken = true)
    {
        shiftedToken = null;
        parsed = false;
        if (!TryParseReferenceToken(token, shift, out var reference, out var appliesToSheet, allowBareToken))
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
        out bool appliesToSheet,
        bool allowBareToken = true)
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
        else if (!allowBareToken)
        {
            // R17-form-controls-linkedcell-2: an unqualified/bare token (no "Sheet!" prefix)
            // always targets the reference-owner's OWN sheet, never shift.SheetId. Callers that
            // know the owner lives on a different sheet than the one being structurally edited
            // (e.g. ShiftCrossSheetFormControlRefs) pass allowBareToken:false so a bare token is
            // left completely untouched instead of being misinterpreted as shift.SheetId.
            appliesToSheet = false;
            return false;
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
    IReadOnlyCollection<uint> CollapsedAnchorRows,
    IReadOnlyCollection<uint> CollapsedAnchorCols,
    IReadOnlyList<GridRange> AllowEditRanges,
    IReadOnlyList<KeyValuePair<GridRange, string?>> AllowEditRangePasswords,
    IReadOnlyList<GridRange> UnlockedAllowEditRanges,
    WorksheetRepeatRange? PrintTitleRows,
    WorksheetRepeatRange? PrintTitleColumns,
    uint FrozenRows,
    uint FrozenCols,
    uint? SplitRow,
    uint? SplitColumn,
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
    IReadOnlyList<FormControlAddressSnapshot> FormControls,
    IReadOnlyList<CrossSheetFormControlRefSnapshot> CrossSheetFormControlRefs,
    IReadOnlyList<CrossSheetPictureRefSnapshot> CrossSheetPictureRefs)
{
    /// <summary>
    /// Records what <see cref="RowColumnShiftHelpers.ShiftWatchedCells"/> produced from
    /// <see cref="WatchedCells"/> the one time the owning command's structural edit (insert/delete
    /// row or column) was applied. Populated by <see cref="RowColumnShiftHelpers.ShiftAddressBearingState"/>
    /// and consulted by <see cref="RowColumnShiftHelpers.RestoreAddressBearingState"/> so that undo can
    /// tell "watches the shift itself moved/dropped" apart from "watches the user added or removed via
    /// the Watch Window after the command ran" (the latter are never wrapped in an <see cref="IWorkbookCommand"/>
    /// and so must not be silently discarded/resurrected by an unrelated undo).
    /// </summary>
    internal IReadOnlyList<(CellAddress Original, CellAddress? Shifted)>? PostShiftWatchedCells { get; set; }
}

internal readonly record struct StyleOnlyEntry(uint Row, uint Col, StyleId StyleId);

// R86-commands-insert-move-refadjust-5-2: Width/Height are captured alongside Anchor so a row/column
// insert or delete that grows/shrinks the object's span (see ResizeSpanForShift) can be undone back
// to its exact pre-edit size, not just its pre-edit anchor cell.
internal readonly record struct TextBoxAddressSnapshot(TextBoxModel TextBox, CellAddress Anchor, double Width, double Height);

internal readonly record struct DrawingShapeAddressSnapshot(DrawingShapeModel Shape, CellAddress Anchor, double Width, double Height);

internal readonly record struct PictureAddressSnapshot(
    PictureModel Picture,
    CellAddress Anchor,
    GridRange? LinkedSourceRange,
    bool IsLinkedToSourceRange,
    uint SourceRowCount,
    uint SourceColumnCount,
    IReadOnlyList<PictureCellSnapshot> Cells,
    double Width,
    double Height);

internal readonly record struct SparklineAddressSnapshot(
    SparklineModel Sparkline,
    GridRange DataRange,
    CellAddress Location);

internal readonly record struct PivotTableAddressSnapshot(
    PivotTableModel PivotTable,
    GridRange SourceRange,
    GridRange TargetRange,
    GridRange? LastRenderedRange,
    SheetId HostSheet);

internal readonly record struct PivotCacheSourceSnapshot(
    PivotCacheModel Cache,
    string? SourceSheetName,
    string? SourceReference,
    // R107-round2: captured BEFORE any shift runs, so RestoreAddressBearingState can put
    // SourceTableId back to whatever it was pre-Apply -- undoing ShiftStructuredTables's orphan pin
    // (see its doc comment) without a separate tracked list, exactly like SourceReference already does
    // for ShiftPivotCaches's own mutation.
    int? SourceTableId);

internal readonly record struct FormControlAddressSnapshot(
    FormControlModel Control,
    GridRange? Anchor,
    DrawingAnchorRange? AnchorOffsets,
    string? LinkedCell,
    string? ListFillRange);

internal readonly record struct CrossSheetFormControlRefSnapshot(
    FormControlModel Control,
    string? LinkedCell,
    string? ListFillRange);

internal readonly record struct CrossSheetPictureRefSnapshot(
    PictureModel Picture,
    GridRange LinkedSourceRange,
    bool IsLinkedToSourceRange,
    uint SourceRowCount,
    uint SourceColumnCount,
    IReadOnlyList<PictureCellSnapshot> Cells);
