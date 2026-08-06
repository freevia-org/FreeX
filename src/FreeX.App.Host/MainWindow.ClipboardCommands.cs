using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Editing;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const string InternalClipboardFormat = "FreeX.InternalClipboard";

    // SourceAreas records every area of a Ctrl+click multi-area selection that was actually copied
    // (R49-render-multiarea-selection-3-1); null means "just SourceRange", so existing call sites
    // that never touch this field (e.g. MainWindow.ScreenshotTour.cs's seeded clipboard) keep their
    // original single-area behavior unchanged.
    private record InternalClipboard(
        GridRange SourceRange,
        List<(CellAddress Source, Cell Cell)> Cells,
        List<(CellAddress Source, PictureCellSnapshot Snapshot)> PictureCells,
        string Text,
        bool IsCut = false,
        IReadOnlyList<GridRange>? SourceAreas = null,
        string? Token = null);
    private InternalClipboard? _internalClipboard;

    private readonly DrawingObjectClipboardSession _drawingObjectClipboard = new();

    private void CancelCopyAndTransientModes()
    {
        ClearClipboardVisualState();
        _internalClipboard = null;
        _drawingObjectClipboard.Clear();
        CancelFormatPainter();
        _borderDrawMode = BorderDrawMode.None;
        SetSelectionMode(ExcelSelectionMode.Normal);
        SetEndMode(false);
    }

    private void ClearClipboardVisualState()
    {
        SheetGrid.ClipboardRange = null;
        SheetGrid.ClipboardRanges = null;
        SheetGrid.ClipboardIsCut = false;
    }

    // ── Ribbon clipboard ─────────────────────────────────────────────────────

    private void CutBtn_Click(object sender, RoutedEventArgs e)   { ExecuteCopy(isCut: true); }
    private void CopyBtn_Click(object sender, RoutedEventArgs e)  { ExecuteCopy(); }
    private void PasteBtn_Click(object sender, RoutedEventArgs e) { ExecutePaste(); }

    private void PasteMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePaste();

    private void PasteValuesMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePaste(PasteMode.Values);

    private void PasteFormulasMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePaste(PasteMode.Formulas);

    private void PasteFormattingMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePaste(PasteMode.Formats);

    private void PasteKeepSourceColumnWidthsMenuItem_Click(object sender, RoutedEventArgs e) =>
        ExecutePaste(PasteMode.All, keepColumnWidths: true);

    private void PasteValuesAndSourceFormattingMenuItem_Click(object sender, RoutedEventArgs e) =>
        ExecutePaste(PasteMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndSourceFormatting));

    private void PasteTransposeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ExecutePaste(PasteMode.All, new PasteSpecialOptions(Transpose: true));

    private void PasteLinkMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePasteLink(transpose: false);

    private void PastePictureMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePasteAsPicture(isLinkedPicture: false);

    private void PasteLinkedPictureMenuItem_Click(object sender, RoutedEventArgs e) => ExecutePasteAsPicture(isLinkedPicture: true);

    private void ExecuteCopy(bool isCut = false)
    {
        // Route a selected drawing object through its own clipboard instead of falling into the
        // cell-range path. Object Cut stays pending until Paste executes the shared move command.
        _drawingObjectClipboard.Clear();
        if (TryCopySelectedDrawingObject(isCut))
            return;

        if (SheetGrid.SelectedRange is not { } range) return;
        var viewport = SheetGrid.Viewport;
        if (viewport == null) return;

        // A Ctrl+click multi-area selection must be copied as a WHOLE, not just its active area
        // (R49-render-multiarea-selection-3-1). GetCurrentSelectionRanges already resolves
        // SheetGrid.SelectedRanges (which includes the active area as its own last entry) with a
        // single-range fallback, matching the pattern the Clear/Format commands already use for
        // the identical multi-area scenario. `range` (a single rectangle) below now only serves as
        // the bounding box of every selected area — for the common single-area case that bounding
        // box IS `range` itself, so behavior is unchanged.
        var areas = GetCurrentSelectionRanges(range);

        // R82-commands-cutcopy-clipboard-5-1: real Excel refuses a Cut on ANY multi-area
        // (Ctrl+click) selection outright, and refuses a Copy unless every area shares the same
        // rows (they combine side-by-side) or the same columns (they combine stacked) --
        // MultiRangeCopyPlanner already encodes this exact rule for the Avalonia shell's
        // WorkbookSession.TryCopySelectedRangeText/TryCutSelectedRangeText. Reject BEFORE touching
        // the OS clipboard or the internal clipboard/marquee state, so a rejected attempt leaves
        // whatever was already copied/cut untouched, matching Excel's "That command cannot be
        // used on multiple selections" behavior instead of silently copying a nonsensical
        // bounding-box marquee.
        if (areas.Count > 1 && (isCut || !MultiRangeCopyPlanner.TryPlan(areas, out _)))
        {
            ShowCommandError(
                new CommandOutcome(false, CreateMultiRangeClipboardError(isCut ? "Cut" : "Copy")),
                isCut ? "Cut" : "Copy");
            return;
        }

        var boundingRange = GetSelectionBoundingRange(areas, range);
        var sheet = _workbook.GetSheet(_currentSheetId);

        // R72-services-clipboard-interop-4-1: Ctrl+C on a column/row header selects the FULL
        // 1..MaxRow or 1..MaxCol extent, not just the sheet's actual data. Without clamping, every
        // step below -- the viewport build, the plain-text/CSV serialization, and the CF_HTML table
        // -- would materialize up to 1,048,576 rows worth of DisplayCells, a multi-second/multi-MB
        // stall for a single-column copy. Real Excel (and FreeX's own export paths, e.g.
        // WorksheetPrintRenderPlanner.ResolveUsedRange/HtmlTableWriter) bound a whole-column/row
        // operation to the sheet's used-range extent instead. Only the ordinary single-area
        // selection is clamped here -- a multi-area (Ctrl+click) whole-column/row selection is rare
        // enough, and its per-area SourceAreas/gap bookkeeping (R49-render-multiarea-selection-3-1)
        // intricate enough, that it is left unclamped, matching its pre-existing behavior.
        var copyRange = areas.Count == 1 ? ClampCopyRangeToUsedRange(sheet, boundingRange) : boundingRange;
        IReadOnlyList<GridRange> copyAreas = areas.Count == 1 ? [copyRange] : areas;

        // P41: SheetGrid.Viewport only materializes the on-screen scroll position (see
        // ViewportService.Metrics BuildFrozenAwareRowMetrics, which stops once it has covered the
        // visible height/width). Serializing/HTML-rendering directly off that viewport truncates
        // any part of the copied range that falls outside the current scroll position to blank —
        // both for the plain-text/CF_HTML clipboard payload placed on the OS clipboard for external
        // paste, and would silently corrupt internal same-instance paste too if not for the
        // clip.Cells fallback captured further below. Build a viewport request sized to the actual
        // copied range instead, so external copy/paste (and CF_HTML) always reflects the full
        // selection regardless of what is currently scrolled into view.
        var fullRangeViewport = BuildFullRangeViewportForClipboard(copyRange) ?? viewport;

        var text = ClipboardSerializer.Serialize(fullRangeViewport, copyRange);
        var clipboardToken = Guid.NewGuid().ToString("N");
        // Place plain text AND an HTML table fragment (CF_HTML) on the OS clipboard together,
        // matching real Excel: destination apps that understand HTML (Word, Outlook, browsers,
        // LibreOffice Calc) pick the richer format and preserve bold/fill/merges/number-format
        // display text, while anything HTML-unaware still gets the existing plain TSV text (M7).
        var data = new DataObject();
        data.SetText(text);
        data.SetData(InternalClipboardFormat, clipboardToken);
        var html = BuildHtmlClipboardFragment(fullRangeViewport, sheet, copyRange, _workbook.Theme);
        if (!string.IsNullOrEmpty(html))
            data.SetData(System.Windows.DataFormats.Html, html);

        // R57-services-clipboard-formats-5-3: real Excel places a comma-delimited "CSV" clipboard
        // format alongside Text/Unicode Text/HTML on every cell-range copy, so a destination that
        // specifically enumerates for CSV (skipping plain Text) still gets a payload. Re-parse the
        // already-built TSV/newline `text` (same field values/escaping semantics as ClipboardSerializer
        // production, just re-delimited) and re-emit it RFC4180-quoted with commas.
        var csv = ClipboardCsvTextRenderer.Render(text);
        if (!string.IsNullOrEmpty(csv))
            data.SetData(System.Windows.DataFormats.CommaSeparatedValue, csv);

        // R91-io-clipboard-image-formats-5-3: real Excel places a rendered picture (CF_ENHMETAFILE /
        // CF_BITMAP) on the clipboard alongside Text/HTML/CSV on EVERY plain range copy, so a
        // destination that only accepts an image (Paint, an image well in another app, an image-only
        // paste target) still gets something instead of nothing. FreeX had no picture flavor at all on
        // a normal copy and no "Copy as Picture" command. A full "Copy as Picture" ribbon dropdown
        // (Appearance: as-shown-on-screen vs as-printed, x Format: vector Picture/EMF vs raster Bitmap)
        // is a larger follow-up (see round summary); this renders a simple bordered-grid Bitmap of the
        // copied cells' own display text and places it under DataFormats.Bitmap, so the "at minimum
        // offer a picture flavour" bar is met for every copy without depending on the shared
        // print/grid rendering pipeline other in-flight work is currently touching.
        if (TryRenderClipboardRangeBitmap(ClipboardSerializer.Deserialize(text)) is { } clipboardBitmap)
            data.SetImage(clipboardBitmap);

        SetClipboardDataWithRetry(data, text);

        // Show marching ants around the copied range(s). ClipboardRange stays the bounding box (used
        // as the sheet-affinity check and by the internal-paste "preserve visual" path), while
        // ClipboardRanges carries every individual area of a Ctrl+click multi-area copy so GridView
        // (App.UI) can stroke ants around each one instead of the bounding box's untouched gaps
        // (R75-render-selection-marquee-4-3).
        SheetGrid.ClipboardRange = copyRange;
        SheetGrid.ClipboardRanges = areas.Count > 1 ? areas : null;
        SheetGrid.ClipboardIsCut = isCut;

        // Capture raw cells (including formulas) for paste formula adjustment -- from EVERY
        // selected area, not just the active one, de-duplicating in case areas ever overlap.
        // copyAreas (not the raw areas) is used here too, so a whole-column/row copy's internal
        // clipboard is bounded by the same used-range clamp as the OS-clipboard payload above,
        // instead of materializing a blank Cell for every one of up to 1,048,576 rows.
        var clipCells = new List<(CellAddress, Cell)>();
        var seenAddresses = new HashSet<CellAddress>();
        foreach (var area in copyAreas)
        {
            for (uint r = area.Start.Row; r <= area.End.Row; r++)
            {
                // R82-commands-cutcopy-clipboard-5-2: real Excel implicitly restricts copying a
                // FILTERED range to its VISIBLE rows only -- rows hidden by AutoFilter are never
                // reproduced at the paste destination -- but never applies this restriction to a
                // plain manually-hidden or group-collapsed row (those DO get copied). Sheet's own
                // IsRowFilterHidden (as opposed to the broader IsRowEffectivelyHidden, which folds
                // every hiding mechanism together) exists precisely to preserve that distinction.
                // Skipping the row here leaves its addresses absent from clipCells, exactly like
                // the "gap" cells between disjoint multi-area copies above -- PasteCommandFactory's
                // internal-paste path already never writes to a destination cell whose source
                // address is missing from this list.
                if (sheet is not null && sheet.IsRowFilterHidden(r))
                    continue;

                for (uint c = area.Start.Col; c <= area.End.Col; c++)
                {
                    var addr = new CellAddress(_currentSheetId, r, c);
                    if (!seenAddresses.Add(addr))
                        continue;
                    var cell = sheet?.GetCell(r, c);
                    clipCells.Add((addr, cell?.Clone() ?? Cell.FromValue(BlankValue.Instance)));
                }
            }
        }
        var pictureCells = CapturePictureCells(fullRangeViewport, sheet, copyRange);
        _internalClipboard = new InternalClipboard(
            copyRange,
            clipCells,
            pictureCells,
            text,
            isCut,
            areas.Count > 1 ? areas : null,
            clipboardToken);
    }

    /// <summary>
    /// R91-io-clipboard-image-formats-5-1 (Chart/Shape), completed for Picture/TextBox by
    /// R92-consumer-wiring-sweep-2: captures a selected chart/shape/picture/text box into
    /// <see cref="_drawingObjectClipboard"/> instead of the cell-range clipboard, when
    /// SheetGrid currently has an object (not a plain cell) selected. Returns false (leaving both
    /// clipboards untouched) for any other selection kind, which keeps falling through to the
    /// pre-existing cell-range copy behavior unchanged.
    /// </summary>
    private bool TryCopySelectedDrawingObject(bool isCut = false)
    {
        SelectionPaneObjectKind? kind = SheetGrid.SelectedObjectKind switch
        {
            FreeX.App.UI.ObjectKind.Chart => SelectionPaneObjectKind.Chart,
            FreeX.App.UI.ObjectKind.Shape => SelectionPaneObjectKind.Shape,
            FreeX.App.UI.ObjectKind.Picture => SelectionPaneObjectKind.Picture,
            FreeX.App.UI.ObjectKind.TextBox => SelectionPaneObjectKind.TextBox,
            _ => null
        };
        if (!_drawingObjectClipboard.TryCapture(
                _currentSheetId,
                kind,
                SheetGrid.SelectedObjectId,
                isCut))
            return false;

        _internalClipboard = null;
        ClearClipboardVisualState();
        return true;
    }

    /// <summary>
    /// R91-io-clipboard-image-formats-5-1: the Ctrl+V side of <see cref="TryCopySelectedDrawingObject"/> --
    /// duplicates or moves the copied/cut chart, shape, picture, or text box onto the CURRENT sheet
    /// (which may be a different sheet than the source if the user switched sheets between commands)
    /// via <see cref="DuplicateDrawingObjectCommand"/>, then selects the new object exactly like
    /// freshly inserting one does (SelectInsertedChart/SelectInsertedDrawingObject).
    /// </summary>
    private void PasteClipboardObject(DrawingObjectClipboardSnapshot objectClip)
    {
        var destinationSheetId = _currentSheetId;
        DuplicateDrawingObjectCommand? command = null;
        IWorkbookCommand CreateCommand()
        {
            command = DrawingObjectClipboardSession.CreatePasteCommand(objectClip, destinationSheetId);
            return command;
        }

        if (!TryExecuteRepeatableCommand(CreateCommand, "Paste", out _))
            return;

        if (objectClip.IsCut)
            _drawingObjectClipboard.CompletePaste(objectClip);

        if (command?.NewObjectId is { } newObjectId)
        {
            var selection = DrawingObjectClipboardSession.CreatePasteSelectionPlan(
                _workbook.GetSheet(destinationSheetId),
                destinationSheetId,
                objectClip.Kind,
                newObjectId);
            if (selection.Kind == SelectionPaneObjectKind.Chart)
            {
                SelectInsertedChart(selection.ObjectId);
            }
            else
            {
                var nativeKind = selection.Kind switch
                {
                    SelectionPaneObjectKind.Shape => FreeX.App.UI.ObjectKind.Shape,
                    SelectionPaneObjectKind.Picture => FreeX.App.UI.ObjectKind.Picture,
                    SelectionPaneObjectKind.TextBox => FreeX.App.UI.ObjectKind.TextBox,
                    _ => FreeX.App.UI.ObjectKind.None,
                };
                if (nativeKind != FreeX.App.UI.ObjectKind.None)
                    SelectInsertedDrawingObject(selection.ObjectId, nativeKind, selection.Anchor);
            }
        }

        UpdateViewport();
        RefreshToolbar();
    }

    /// <summary>Matches the phrasing of WorkbookSession's (Avalonia-facing) identical
    /// CreateMultiRangeClipboardError helper, kept as a separate literal here rather than shared
    /// since that helper is a private instance member of WorkbookSession.</summary>
    private static string CreateMultiRangeClipboardError(string operation) =>
        operation + " does not support multiple selected ranges yet.";

    /// <summary>
    /// R82-commands-cutcopy-clipboard-5-3: real Excel invalidates the OS clipboard once a
    /// Cut-then-Paste move completes -- the marching ants disappear and any further Ctrl+V is a
    /// no-op. Without this, the TSV/HTML payload <see cref="SetClipboardDataWithRetry"/> placed on
    /// the real OS clipboard during the original Ctrl+X stays there untouched even after
    /// <c>_internalClipboard</c> is cleared below, so <c>ExecutePaste</c>'s external-clipboard
    /// fallback (<see cref="TryGetClipboardText"/>/<see cref="TryGetClipboardHtml"/>) would happily
    /// paste that same cut content a second time. Best-effort: a transiently locked clipboard just
    /// leaves the stale cut text in place, matching how the other clipboard helpers in this file
    /// already treat OS-clipboard access as fallible.
    /// </summary>
    private static void InvalidateOsClipboardAfterCutMove()
    {
        const int attempts = 20;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                System.Windows.Clipboard.Clear();
                return;
            }
            catch (ExternalException) when (attempt < attempts)
            {
                Thread.Sleep(50);
            }
            catch
            {
                return;
            }
        }
    }

    private static void SetClipboardDataWithRetry(DataObject data, string text)
    {
        const int attempts = 20;
        var requiresImage = data.GetDataPresent(System.Windows.DataFormats.Bitmap);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(data, copy: true);
                System.Windows.Clipboard.Flush();
                if (System.Windows.Clipboard.GetText() == text
                    && (!requiresImage || System.Windows.Clipboard.GetImage() is not null))
                    return;
            }
            catch (ExternalException) when (attempt < attempts)
            {
            }
            catch
            {
                break;
            }

            if (attempt < attempts)
                Thread.Sleep(50);
        }

        // Some clipboard providers reject richer formats even after the lock clears.
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                System.Windows.Clipboard.Flush();
                if (System.Windows.Clipboard.GetText() == text)
                    return;
            }
            catch (ExternalException) when (attempt < attempts)
            {
            }
            catch
            {
                return;
            }

            if (attempt < attempts)
                Thread.Sleep(50);
        }
    }

    /// <summary>
    /// R72-services-clipboard-interop-4-1: clamps a whole-column (<paramref name="range"/> spans
    /// every row, 1..<see cref="CellAddress.MaxRow"/>) or whole-row (spans every column,
    /// 1..<see cref="CellAddress.MaxCol"/>) copy selection down to the sheet's used-range extent on
    /// that axis, mirroring <c>WorksheetPrintRenderPlanner.ResolveUsedRange</c>'s identical
    /// whole-sheet-extent clamp for print/export. An explicit, already-bounded selection (the
    /// overwhelmingly common case, e.g. A1:C3) is returned unchanged. An empty sheet (no used range)
    /// clamps down to the single top-left cell of the selection, since there is nothing to copy.
    /// </summary>
    private static GridRange ClampCopyRangeToUsedRange(Sheet? sheet, GridRange range)
    {
        var isWholeColumn = range.RowCount == CellAddress.MaxRow;
        var isWholeRow = range.ColCount == CellAddress.MaxCol;
        if (!isWholeColumn && !isWholeRow)
            return range;

        if (sheet?.GetUsedRange() is not { } used)
            return new GridRange(range.Start, range.Start);

        var startRow = isWholeColumn ? Math.Max(range.Start.Row, used.Start.Row) : range.Start.Row;
        var endRow = isWholeColumn ? Math.Min(range.End.Row, used.End.Row) : range.End.Row;
        var startCol = isWholeRow ? Math.Max(range.Start.Col, used.Start.Col) : range.Start.Col;
        var endCol = isWholeRow ? Math.Min(range.End.Col, used.End.Col) : range.End.Col;

        // Defensive: a whole-column/row selection always spans 1..Max on its own axis, so it can
        // never actually fail to overlap the used range on that axis -- but keep the result
        // well-formed if that invariant is ever violated by a future caller.
        if (startRow > endRow) { startRow = range.Start.Row; endRow = range.Start.Row; }
        if (startCol > endCol) { startCol = range.Start.Col; endCol = range.Start.Col; }

        return new GridRange(
            new CellAddress(range.Start.Sheet, startRow, startCol),
            new CellAddress(range.Start.Sheet, endRow, endCol));
    }

    // Smallest GridRange enclosing every area in `areas` (all assumed to be on the same sheet).
    // For a single-area selection this is exactly that area; PasteCommandFactory's internal-paste
    // path shifts each captured cell by its own offset from this bounding box's start, so cells
    // that fall inside the box but outside every actual area (the "gaps" between disjoint areas)
    // are simply absent from clip.Cells and are never written to on paste -- matching Excel's own
    // preserved-relative-layout behavior for a non-contiguous copy.
    private static GridRange GetSelectionBoundingRange(IReadOnlyList<GridRange> areas, GridRange fallback)
    {
        if (areas.Count == 0)
            return fallback;

        var sheetId = areas[0].Start.Sheet;
        var minRow = areas[0].Start.Row;
        var minCol = areas[0].Start.Col;
        var maxRow = areas[0].End.Row;
        var maxCol = areas[0].End.Col;
        for (var i = 1; i < areas.Count; i++)
        {
            var area = areas[i];
            if (area.Start.Row < minRow) minRow = area.Start.Row;
            if (area.Start.Col < minCol) minCol = area.Start.Col;
            if (area.End.Row > maxRow) maxRow = area.End.Row;
            if (area.End.Col > maxCol) maxCol = area.End.Col;
        }

        return new GridRange(
            new CellAddress(sheetId, minRow, minCol),
            new CellAddress(sheetId, maxRow, maxCol));
    }

    /// <summary>
    /// Builds a <see cref="ViewportModel"/> that materializes every cell in <paramref name="range"/>,
    /// independent of the current scroll position (P41). <see cref="SheetGrid"/>'s live viewport is
    /// built from a <c>ViewportRequest</c> sized to the on-screen scroll area (see
    /// <c>MainWindow.CreateViewport</c>/<c>ViewportService.Metrics.BuildFrozenAwareRowMetrics</c>,
    /// which stops materializing rows/columns once it has covered that on-screen height/width) — a
    /// selection extending past the visible viewport would otherwise serialize as blank for every
    /// off-screen cell, both to the OS clipboard (plain text + CF_HTML) and to the internal picture
    /// snapshot. Requesting a viewport whose top-left is the copied range's own start and whose
    /// available height/width is sized (generously) to the range's own row/column span, mirroring
    /// how <see cref="PrintRenderer.RenderWorksheet"/> requests a viewport sized to the print range
    /// rather than the on-screen area, guarantees every cell in the range is present regardless of
    /// what is currently scrolled into view. Returns null (falling back to the on-screen viewport)
    /// if the current sheet cannot be resolved.
    /// </summary>
    private ViewportModel? BuildFullRangeViewportForClipboard(GridRange range)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return null;

        // Generous per-row/per-column pixel bounds so the viewport's internal "stop materializing"
        // heuristic (which walks actual row heights/column widths, not these estimates) always
        // reaches past the end of the requested range even for tall rows / wide columns, while still
        // being a small constant multiple of the range size rather than the whole sheet.
        const double MaxPlausibleRowHeight = 500.0;
        const double MaxPlausibleColWidth = 2000.0;

        var rowSpan = (double)range.RowCount;
        var colSpan = (double)range.ColCount;
        var availableHeight = Math.Min(double.MaxValue / 2, (rowSpan + 2) * MaxPlausibleRowHeight);
        var availableWidth = Math.Min(double.MaxValue / 2, (colSpan + 2) * MaxPlausibleColWidth);

        var request = new ViewportRequest(
            TopRow: range.Start.Row,
            LeftCol: range.Start.Col,
            AvailableHeight: availableHeight,
            AvailableWidth: availableWidth,
            IncludeObjects: false,
            SplitPaneOffsets: null);

        return _viewportService.GetViewport(_workbook, _currentSheetId, request);
    }

    private static List<(CellAddress Source, PictureCellSnapshot Snapshot)> CapturePictureCells(
        ViewportModel viewport,
        Sheet? sheet,
        GridRange range)
    {
        var displayCells = new Dictionary<(uint Row, uint Col), DisplayCell>(viewport.Cells.Count);
        foreach (var cell in viewport.Cells)
            displayCells[(cell.Row, cell.Col)] = cell;

        var result = new List<(CellAddress, PictureCellSnapshot)>();
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
            {
                var address = new CellAddress(range.Start.Sheet, r, c);
                if (displayCells.TryGetValue((r, c), out var displayCell))
                {
                    result.Add((address, CreatePictureSnapshot(range, address, displayCell)));
                    continue;
                }

                var cell = sheet?.GetCell(r, c);
                result.Add((
                    address,
                    new PictureCellSnapshot(
                        r - range.Start.Row,
                        c - range.Start.Col,
                        DrawingInputParser.FormatPictureCellText(cell?.Value ?? BlankValue.Instance),
                        null,
                        cell?.Value is NumberValue or DateTimeValue)));
            }
        }

        return result;
    }

    private static PictureCellSnapshot CreatePictureSnapshot(
        GridRange range,
        CellAddress address,
        DisplayCell cell) =>
        new(
            address.Row - range.Start.Row,
            address.Col - range.Start.Col,
            cell.DisplayText,
            cell.Style?.Clone(),
            cell.RawValue is NumberValue or DateTimeValue);

    private void ExecutePaste(
        PasteMode mode = PasteMode.All,
        PasteSpecialOptions options = default,
        bool keepColumnWidths = false,
        bool externalTextAsText = false)
    {
        // R91-io-clipboard-image-formats-5-1: the Ctrl+V side of a chart/shape Ctrl+C (see
        // TryCopySelectedDrawingObject) -- duplicate the copied object instead of falling through to
        // the cell-range paste logic below, which has no concept of an object clipboard at all.
        if (_drawingObjectClipboard.Content is { } objectClip)
        {
            PasteClipboardObject(objectClip);
            return;
        }

        if (SheetGrid.SelectedRange is not { } range) return;

        string? currentClipboardText = null;
        bool currentClipboardTextRead = false;

        // Paste Special > Text / Unicode Text (Excel semantics: paste the clipboard's plain text
        // only, discarding any FreeX-internal formula/formatting payload) must always go through the
        // external-clipboard plain-text path below, even right after an in-app copy where the OS
        // clipboard text still matches the internal clipboard's text. Without this early bypass, the
        // internal-clipboard branch below wins (its text-equality check can't tell "explicitly asked
        // for text" from "clipboard unchanged") and silently performs a full formatted internal
        // paste instead (review P44).
        if (!externalTextAsText && _internalClipboard is { } clip)
        {
            var internalClipboardMarkerMatches = clip.Token is not null &&
                string.Equals(TryGetClipboardInternalMarker(), clip.Token, StringComparison.Ordinal);
            var clipboardReadFailed = false;
            ClipboardPastePlan pastePlan;
            if (internalClipboardMarkerMatches)
            {
                // WPF can transiently serve an older/empty text projection while a flushed
                // DataObject is being published. The private marker is the authoritative
                // ownership signal for a same-app copy, so do not misclassify that copy as
                // external based on a racy plain-text read.
                currentClipboardText = clip.Text;
                currentClipboardTextRead = true;
                pastePlan = ClipboardPastePlan.UseInternalClipboard;
            }
            else
            {
                currentClipboardText = TryGetClipboardText(out clipboardReadFailed);
                currentClipboardTextRead = true;
                pastePlan = ClipboardPastePlanner.PlanPaste(clip.Text, currentClipboardText, clipboardReadFailed);
            }
            if (pastePlan == ClipboardPastePlan.ReadFailed)
            {
                // A transient OS-clipboard read failure must not silently fall back to a stale
                // internal paste of the wrong content — skip the paste and tell the user.
                ShowCommandError(
                    new CommandOutcome(false, "The clipboard is busy. Try pasting again."),
                    "Paste");
                return;
            }

            if (pastePlan == ClipboardPastePlan.UseExternalClipboardText)
            {
                _internalClipboard = null;
                ClearClipboardVisualState();
            }
            else
            {
                var expandPasteToSelectedRange = ClipboardPastePlanner.ShouldFillSelectedDestinationRange(clip.IsCut, options);
                IWorkbookCommand CreatePasteCommand()
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var destinationRange = expandPasteToSelectedRange
                        ? currentRange
                        : new GridRange(currentRange.Start, currentRange.Start);

                    if (TryCreateCutMoveCommand(clip, mode, options, keepColumnWidths, currentRange.Start, out var moveCommand))
                    {
                        // Excel cut+paste is a MOVE: the moved formulas keep their own references
                        // unchanged, while OTHER formulas that pointed at the cut cells are rewritten
                        // to follow the move. That is exactly MoveRangeCommand/MoveRangeOp semantics
                        // (already used for the grid drag-and-drop move gesture), so route plain
                        // cut+paste through it instead of the copy-paste-and-clear combo, which would
                        // incorrectly rewrite the moved formulas' own references and never fix up
                        // references from other cells.
                        return moveCommand;
                    }

                    var targetSheetIds = CurrentGroupedEditSheetIds();
                    var pasteCommands = new List<IWorkbookCommand>(targetSheetIds.Count);
                    foreach (var sheetId in targetSheetIds)
                    {
                        var sheetDestinationRange = GroupedSheetRangePlanner.RemapRangeToSheet(destinationRange, sheetId);
                        // R108-clipboard-paste-multiarea-1: forward clip.SourceAreas (mirrors the
                        // Paste-Special-Validation/Format-Painter call sites in this file, e.g.
                        // R78-commands-paste-special-5-1/-3/-4 below) so the r107 plain-Ctrl+V
                        // conditional-format/data-validation carry (PasteCommandFactory's
                        // sourceAreas-aware CF/DV branches) restricts itself to the ACTUAL copied
                        // areas of a multi-area (Ctrl+click) source selection instead of treating
                        // its whole bounding box -- including the untouched gap between disjoint
                        // areas -- as copied.
                        var sheetPasteCommand = PasteCommandFactory.CreateInternalPasteCommand(
                            _workbook,
                            sheetId,
                            clip.SourceRange,
                            clip.Cells,
                            sheetDestinationRange,
                            ClipboardPastePlanner.ToCorePasteMode(mode),
                            options,
                            clip.SourceAreas);
                        if (keepColumnWidths)
                        {
                            sheetPasteCommand = new CompositeWorkbookCommand(
                                "Paste Special",
                                [
                                    sheetPasteCommand,
                                    new PasteColumnWidthsCommand(sheetId, clip.SourceRange, sheetDestinationRange.Start.Col, sheetDestinationRange.ColCount)
                                ]);
                        }

                        pasteCommands.Add(sheetPasteCommand);
                    }

                    var pasteLabel = mode == PasteMode.All && options == default && !keepColumnWidths
                        ? "Paste"
                        : "Paste Special";
                    var command = pasteCommands.Count == 1
                        ? pasteCommands[0]
                        : new CompositeWorkbookCommand(pasteLabel, pasteCommands);

                    if (ClipboardPastePlanner.ShouldClearCutSourceAfterPaste(
                            clip.IsCut,
                            clip.SourceRange,
                            currentRange,
                            mode,
                            options,
                            keepColumnWidths))
                    {
                        // A multi-area Cut's SourceRange is only the bounding box of every copied
                        // area (R49-render-multiarea-selection-3-1) -- clearing that whole box would
                        // wipe the "gap" cells between areas that were never part of the cut
                        // selection. Clear each actual source area individually instead; for the
                        // ordinary single-area case SourceAreas is null and this is exactly the
                        // previous single ClearContentsCommand(clip.SourceRange) behavior.
                        var sourceAreas = clip.SourceAreas ?? [clip.SourceRange];
                        var clearCommands = sourceAreas
                            .Select(area => (IWorkbookCommand)new ClearContentsCommand(area.Start.Sheet, area))
                            .ToList();
                        command = new CompositeWorkbookCommand(
                            "Cut and Paste",
                            [command, .. clearCommands]);
                    }

                    return command;
                }

                var title = mode == PasteMode.All && !options.Transpose && options.Operation == PasteSpecialOperation.None
                    ? "Paste"
                    : "Paste Special";

                if (!TryExecuteRepeatableCommand(CreatePasteCommand, title, out _))
                    return;

                var preserveClipboardVisual = ClipboardPastePlanner.ShouldPreserveClipboardVisualAfterPaste(clip.IsCut);
                _repeatPostAction = _ =>
                {
                    CompletePasteSelection(
                        clip.SourceRange,
                        options,
                        preserveClipboardVisual,
                        expandToSelectedRange: expandPasteToSelectedRange);
                    if (clip.IsCut)
                    {
                        _internalClipboard = null;
                        InvalidateOsClipboardAfterCutMove();
                    }
                };
                CompletePasteSelection(
                    clip.SourceRange,
                    options,
                    preserveClipboardVisual,
                    expandToSelectedRange: expandPasteToSelectedRange);
                if (clip.IsCut)
                {
                    _internalClipboard = null;
                    InvalidateOsClipboardAfterCutMove();
                }
                UpdateViewport();
                RefreshToolbar();
                return;
            }
        }

        if (mode == PasteMode.Formats || mode == PasteMode.Formulas)
            return;

        var text = currentClipboardTextRead ? currentClipboardText : TryGetClipboardText();
        if (ClipboardPastePlanner.ShouldPasteClipboardImageForNormalPaste(
                mode,
                text,
                TryClipboardContainsImage()) &&
            TryPasteClipboardImage(range.Start))
            return;

        // Fallback: external clipboard (plain text, or HTML table structure when available)
        if (string.IsNullOrEmpty(text)) return;

        // Prefer the actual CF_HTML <tr>/<td> row/column structure over the plain-text
        // tab/newline splitter when a web-table (or other HTML-producing app) put HTML on the
        // clipboard alongside its plain-text fallback: DeserializePlainText treats every bare
        // \r/\n as a new row, which misreads a source cell whose rendered text spans multiple
        // lines (e.g. a wrapped address, or an explicit <br>) as a row break, shifting every
        // subsequent row by one (R39-io-external-clipboard-2-3).
        var htmlRows = TryGetClipboardHtml() is { } htmlPayload
            ? TryParseHtmlClipboardTableRows(htmlPayload)
            : null;
        var capturedRows = htmlRows is { Count: > 0 } htmlRowList
            ? htmlRowList
            : ClipboardSerializer.Deserialize(text).Select(row => (IReadOnlyList<string>)row).ToList();
        if (capturedRows.Count == 0 || capturedRows.All(r => r.Count == 0)) return;

        IWorkbookCommand CreateExternalPasteCommand()
        {
            var currentRange = SheetGrid.SelectedRange ?? range;
            return PasteCommandFactory.CreateExternalTextPasteCommand(
                _currentSheetId,
                currentRange,
                capturedRows,
                preserveText: externalTextAsText,
                options);
        }

        if (!TryExecuteRepeatableCommand(CreateExternalPasteCommand, "Paste", out _))
            return;

        _repeatPostAction = _ => CompleteExternalPasteSelection(capturedRows, expandToSelectedRange: true);

        CompleteExternalPasteSelection(capturedRows, expandToSelectedRange: true);
        UpdateViewport();
        RefreshToolbar();
    }

    private static string? TryGetClipboardText() => TryGetClipboardText(out _);

    private static string? TryGetClipboardInternalMarker()
    {
        try
        {
            return System.Windows.Clipboard.GetData(InternalClipboardFormat) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the OS clipboard text, distinguishing "read failed" (clipboard locked by another
    /// process) from "read succeeded but empty/non-text" — the paste planner must skip the paste
    /// on failure instead of falling back to a stale internal-clipboard paste (review P1).
    /// </summary>
    private static string? TryGetClipboardText(out bool readFailed)
    {
        const int attempts = 20;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                readFailed = false;
                return System.Windows.Clipboard.GetText();
            }
            catch (ExternalException) when (attempt < attempts)
            {
                Thread.Sleep(50);
            }
            catch
            {
                break;
            }
        }

        readFailed = true;
        return null;
    }

    private static bool TryClipboardContainsImage()
    {
        try { return System.Windows.Clipboard.ContainsImage(); }
        catch { return false; }
    }

    /// <summary>
    /// R91-io-clipboard-image-formats-5-4: looks for a raw, alpha-preserving "PNG" (or "image/png")
    /// clipboard entry alongside the flattened CF_DIB/CF_BITMAP one that <see cref="System.Windows.Clipboard.GetImage"/>
    /// always resolves to. Chrome/Edge and many image editors place both when copying a
    /// transparent-background image; returns the raw PNG bytes (with alpha intact) when found, or
    /// null when no such format is present/readable so the caller falls back to the flattened image.
    /// </summary>
    private static byte[]? TryGetClipboardPngFormatBytes()
    {
        try
        {
            var dataObject = System.Windows.Clipboard.GetDataObject();
            if (dataObject is null)
                return null;

            foreach (var formatName in PngClipboardFormatNames)
            {
                if (!dataObject.GetDataPresent(formatName))
                    continue;

                var raw = dataObject.GetData(formatName);
                switch (raw)
                {
                    case byte[] bytes when bytes.Length > 0:
                        return bytes;
                    case System.IO.MemoryStream memoryStream:
                        return memoryStream.ToArray();
                    case System.IO.Stream stream:
                        using (stream)
                        {
                            using var buffer = new System.IO.MemoryStream();
                            stream.CopyTo(buffer);
                            if (buffer.Length > 0)
                                return buffer.ToArray();
                        }
                        break;
                }
            }
        }
        catch
        {
            // Fall back to the flattened DIB/Bitmap path below -- a transient clipboard-provider
            // failure on the richer format must not fail the whole paste.
        }

        return null;
    }

    private static readonly string[] PngClipboardFormatNames = ["PNG", "image/png"];

    /// <summary>Reads the CF_HTML clipboard payload (header + fragment), or null when absent/unreadable.</summary>
    private static string? TryGetClipboardHtml()
    {
        try
        {
            return System.Windows.Clipboard.GetData(System.Windows.DataFormats.Html) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a CF_HTML clipboard payload's first &lt;table&gt; into rows of plain cell text (no
    /// per-cell styling), or null if no table markup is found. This only recovers the actual
    /// &lt;tr&gt;/&lt;td&gt;/&lt;th&gt; row/column boundaries -- fuller style reconstruction mirrors
    /// FreeX.Core.IO.HtmlTableReader (used for whole-file HTML import) and is a larger follow-up
    /// out of scope here; this is enough to stop a multi-line source cell's embedded line break
    /// from being misread as a row boundary the way the plain-text tab/newline splitter does
    /// (R39-io-external-clipboard-2-3).
    /// </summary>
    private static List<IReadOnlyList<string>>? TryParseHtmlClipboardTableRows(string htmlPayload)
    {
        var fragment = ExtractHtmlClipboardFragment(htmlPayload);
        var tableInner = ExtractFirstHtmlTableInner(fragment);
        if (tableInner is null)
            return null;

        // R57-services-clipboard-formats-5-2: track column occupancy from an active rowspan the same
        // way FreeX.Core.IO.HtmlTableReader does for whole-file HTML import, so a merged header cell
        // (colspan) or a rowspan-ed cell keeps every column after it lined up with the right data
        // column instead of shifting left. Keyed by 1-based column -> the last (0-based) row index it
        // remains occupied through.
        var rowSpanRemaining = new Dictionary<int, int>();
        var rows = new List<List<string>>();
        var rowIndex = -1;

        foreach (var rowInner in EnumerateHtmlElements(tableInner, "tr"))
        {
            rowIndex++;
            var cells = new List<string>();
            var col = 0;

            foreach (var cellInfo in EnumerateHtmlCells(rowInner))
            {
                col++;
                while (rowSpanRemaining.TryGetValue(col, out var occupiedThroughRow) && occupiedThroughRow >= rowIndex)
                {
                    EnsureHtmlPasteColumn(cells, col);
                    col++;
                }

                var text = DecodeHtmlCellText(cellInfo.InnerHtml);

                // R78-services-clipboard-formats-5-1: a <td> carrying the "mso-number-format:'\@'"
                // marker (written by ClipboardHtmlSerializer.RequiresTextFormatMarker for a Text-typed
                // source cell, and by real Excel for the same reason) must round-trip through this
                // HTML-preferred paste path with the identical leading-apostrophe escape the plain-text
                // clipboard sibling already carries -- otherwise a Text-formatted "00501" silently
                // becomes the number 501 whenever CF_HTML is present (the common case, since ExecuteCopy
                // always places CF_HTML alongside plain text).
                if (cellInfo.IsTextFormat)
                    text = ClipboardSerializer.EscapeTextCellForPaste(text);

                var colSpan = Math.Max(1, cellInfo.ColSpan);
                var rowSpan = Math.Max(1, cellInfo.RowSpan);
                var endCol = col + colSpan - 1;

                // The pasted grid has no merged-cell concept (unlike HtmlTableReader's AddMergedRegion),
                // so repeat the spanned cell's text across every column it covers -- this matches what a
                // merged header cell visually represents, and keeps every subsequent column's data under
                // its own header instead of shifting left by one per colspan.
                for (var c = col; c <= endCol; c++)
                {
                    EnsureHtmlPasteColumn(cells, c);
                    cells[c - 1] = text;
                }

                if (rowSpan > 1)
                {
                    for (var c = col; c <= endCol; c++)
                        rowSpanRemaining[c] = rowIndex + rowSpan - 1;
                }

                col = endCol;
            }

            if (cells.Count > 0)
                rows.Add(cells);
        }

        return rows.Count > 0 ? rows.Cast<IReadOnlyList<string>>().ToList() : null;
    }

    private static void EnsureHtmlPasteColumn(List<string> row, int col)
    {
        while (row.Count < col)
            row.Add(string.Empty);
    }

    /// <summary>CF_HTML wraps the real markup between StartFragment/EndFragment comments after a
    /// small header (see BuildHtmlClipboardFragment for the write side using the same convention);
    /// falls back to the whole payload if the markers are absent (some non-Excel producers omit them).</summary>
    private static string ExtractHtmlClipboardFragment(string html)
    {
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var start = html.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        var end = html.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
        return start >= 0 && end > start
            ? html[(start + startMarker.Length)..end]
            : html;
    }

    private static string? ExtractFirstHtmlTableInner(string html)
    {
        int i = 0;
        while (i < html.Length)
        {
            int lt = html.IndexOf('<', i);
            if (lt < 0)
                return null;
            if (string.Equals(HtmlTagNameAt(html, lt), "table", StringComparison.OrdinalIgnoreCase))
            {
                int tagEnd = html.IndexOf('>', lt);
                if (tagEnd < 0)
                    return null;
                int closeStart = FindMatchingHtmlClose(html, tagEnd + 1, "table");
                return closeStart < 0 ? html[(tagEnd + 1)..] : html[(tagEnd + 1)..closeStart];
            }
            i = lt + 1;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateHtmlElements(string html, string tag)
    {
        int i = 0;
        while (i < html.Length)
        {
            int lt = html.IndexOf('<', i);
            if (lt < 0)
                break;
            if (string.Equals(HtmlTagNameAt(html, lt), tag, StringComparison.OrdinalIgnoreCase))
            {
                int tagEnd = html.IndexOf('>', lt);
                if (tagEnd < 0)
                    break;
                int closeStart = FindMatchingHtmlClose(html, tagEnd + 1, tag);
                string inner = closeStart < 0 ? html[(tagEnd + 1)..] : html[(tagEnd + 1)..closeStart];
                yield return inner;
                i = closeStart < 0 ? html.Length : SkipHtmlClosingTag(html, closeStart);
            }
            else
            {
                i = lt + 1;
            }
        }
    }

    /// <summary>One &lt;td&gt;/&lt;th&gt; cell's inner HTML plus its colspan/rowspan (each defaulted to 1
    /// when absent or non-positive), used by <see cref="TryParseHtmlClipboardTableRows"/> to keep
    /// merged-header columns aligned with their data (R57-services-clipboard-formats-5-2), plus
    /// whether the cell's own style carries the "mso-number-format:'\@'" Text marker
    /// (R78-services-clipboard-formats-5-1).</summary>
    private readonly record struct HtmlCellSpan(string InnerHtml, int ColSpan, int RowSpan, bool IsTextFormat);

    /// <summary>Matches the "mso-number-format" Text (@) marker ClipboardHtmlSerializer writes for a
    /// Text-typed source cell -- and that real Excel writes for the same reason -- regardless of
    /// which quote style wraps the style attribute itself (single vs. double) or the format code
    /// inside it (Excel emits <c>mso-number-format:"\@"</c>; FreeX's own writer emits
    /// <c>mso-number-format:'\@'</c>). Searched directly against the tag's raw attribute text rather
    /// than against an extracted "style" attribute value, since a simple quote-delimited attribute
    /// extractor cannot reliably handle one quote style nested inside the other.</summary>
    private static readonly System.Text.RegularExpressions.Regex MsoTextNumberFormatRegex = new(
        @"mso-number-format\s*:\s*[""']\\?@[""']",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static IEnumerable<HtmlCellSpan> EnumerateHtmlCells(string rowInner)
    {
        int i = 0;
        while (i < rowInner.Length)
        {
            int lt = rowInner.IndexOf('<', i);
            if (lt < 0)
                break;
            var name = HtmlTagNameAt(rowInner, lt);
            if (name is "td" or "th")
            {
                int tagEnd = rowInner.IndexOf('>', lt);
                if (tagEnd < 0)
                    break;
                var tagContent = rowInner[(lt + 1)..tagEnd];
                var colSpan = ParseHtmlSpanAttribute(tagContent, "colspan");
                var rowSpan = ParseHtmlSpanAttribute(tagContent, "rowspan");
                var isTextFormat = MsoTextNumberFormatRegex.IsMatch(tagContent);
                int closeStart = FindMatchingHtmlClose(rowInner, tagEnd + 1, name);
                string inner = closeStart < 0 ? rowInner[(tagEnd + 1)..] : rowInner[(tagEnd + 1)..closeStart];
                yield return new HtmlCellSpan(inner, colSpan, rowSpan, isTextFormat);
                i = closeStart < 0 ? rowInner.Length : SkipHtmlClosingTag(rowInner, closeStart);
            }
            else
            {
                i = lt + 1;
            }
        }
    }

    /// <summary>Reads a numeric attribute (e.g. <c>colspan="2"</c>, <c>colspan=2</c>, or unquoted/single
    /// quoted) from a tag's raw attribute text. Returns 1 (the "no span" default) if absent, malformed,
    /// or non-positive.</summary>
    /// <summary>
    /// Upper bound for a single HTML <c>colspan</c>/<c>rowspan</c> when pasting. A span wider than the
    /// sheet itself cannot produce anything pasteable, so this caps the expansion work instead.
    /// </summary>
    private const int MaxHtmlPasteSpan = (int)CellAddress.MaxCol;

    private static int ParseHtmlSpanAttribute(string tagContent, string attributeName)
    {
        var searchFrom = 0;
        while (searchFrom < tagContent.Length)
        {
            var idx = tagContent.IndexOf(attributeName, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return 1;

            var afterIdx = idx + attributeName.Length;
            var boundaryOk = idx == 0 || char.IsWhiteSpace(tagContent[idx - 1]);
            if (!boundaryOk)
            {
                searchFrom = afterIdx;
                continue;
            }

            var p = afterIdx;
            while (p < tagContent.Length && char.IsWhiteSpace(tagContent[p]))
                p++;
            if (p >= tagContent.Length || tagContent[p] != '=')
            {
                searchFrom = afterIdx;
                continue;
            }

            p++;
            while (p < tagContent.Length && char.IsWhiteSpace(tagContent[p]))
                p++;
            if (p < tagContent.Length && (tagContent[p] == '"' || tagContent[p] == '\''))
                p++;

            var digitsStart = p;
            while (p < tagContent.Length && char.IsDigit(tagContent[p]))
                p++;

            // Clamp, don't just reject non-positive values. The caller expands a span into that many
            // columns/rows, so an arbitrarily large colspan/rowspan ("<td colspan='500000000'>" from a
            // hostile or merely buggy page — a page's copy handler can put any HTML on the clipboard)
            // turned Ctrl+V into hundreds of millions of list operations on the UI thread, hanging and
            // then killing the app with OutOfMemoryException. Nothing beyond the sheet's own limits can
            // be pasted anyway, so cap the span there.
            return int.TryParse(
                tagContent[digitsStart..p],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value) && value > 0
                ? Math.Min(value, MaxHtmlPasteSpan)
                : 1;
        }

        return 1;
    }

    private static string? HtmlTagNameAt(string s, int ltIndex)
    {
        int i = ltIndex + 1;
        if (i < s.Length && s[i] == '/')
            i++;
        int start = i;
        while (i < s.Length && char.IsLetterOrDigit(s[i]))
            i++;
        return i > start ? s[start..i].ToLowerInvariant() : null;
    }

    /// <summary>Finds the index of the matching &lt;/tag&gt;, honoring nesting. -1 if none.</summary>
    private static int FindMatchingHtmlClose(string s, int from, string tag)
    {
        int depth = 0;
        int i = from;
        while (i < s.Length)
        {
            int lt = s.IndexOf('<', i);
            if (lt < 0)
                return -1;
            bool isClose = lt + 1 < s.Length && s[lt + 1] == '/';
            var name = HtmlTagNameAt(s, lt);
            if (string.Equals(name, tag, StringComparison.OrdinalIgnoreCase))
            {
                if (isClose)
                {
                    if (depth == 0)
                        return lt;
                    depth--;
                }
                else if (!IsHtmlSelfClosing(s, lt))
                {
                    depth++;
                }
            }
            i = lt + 1;
        }
        return -1;
    }

    private static bool IsHtmlSelfClosing(string s, int lt)
    {
        int gt = s.IndexOf('>', lt);
        return gt > lt && s[gt - 1] == '/';
    }

    private static int SkipHtmlClosingTag(string s, int closeStart)
    {
        int gt = s.IndexOf('>', closeStart);
        return gt < 0 ? s.Length : gt + 1;
    }

    /// <summary>Strips tags from a cell's inner HTML -- turning &lt;br&gt; into a literal newline
    /// kept WITHIN the cell's own text (never a row separator), and an &lt;img&gt; into its alt text
    /// when present (R78-services-clipboard-formats-5-3, see below) -- decodes entities, and
    /// trims.</summary>
    private static string DecodeHtmlCellText(string innerHtml)
    {
        var sb = new StringBuilder(innerHtml.Length);
        int i = 0;
        while (i < innerHtml.Length)
        {
            char c = innerHtml[i];
            if (c == '<')
            {
                var name = HtmlTagNameAt(innerHtml, i);
                int gt = innerHtml.IndexOf('>', i);
                if (gt < 0)
                    break;
                if (name is "br")
                {
                    sb.Append('\n');
                }
                else if (name is "img")
                {
                    // Full picture-paste (fetching/decoding the src and creating a floating Picture
                    // object the way TryPasteClipboardImage does for a pure CF_Bitmap payload) is a
                    // larger follow-up out of scope here. But silently emptying the cell would lose
                    // the only content the source page associated with it (e.g. a product thumbnail
                    // next to its price) with no way to recover it from the paste and no user-visible
                    // sign anything was dropped. Falling back to the img's alt text -- the HTML
                    // author's own stand-in for the image's content -- keeps that content in the
                    // pasted cell instead of a blank, unexplained gap.
                    var alt = ExtractHtmlAttributeValue(innerHtml[(i + 1)..gt], "alt");
                    if (!string.IsNullOrEmpty(alt))
                        sb.Append(alt);
                }
                i = gt + 1;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return System.Net.WebUtility.HtmlDecode(sb.ToString()).Trim();
    }

    /// <summary>Reads a quoted string attribute's value (e.g. <c>alt="Widget"</c>) from a tag's raw
    /// attribute text. Returns null if absent or malformed (unquoted/unterminated). Mirrors
    /// <see cref="ParseHtmlSpanAttribute"/>'s boundary-checked forward search but reads an arbitrary
    /// quoted value instead of a trailing digit run.</summary>
    private static string? ExtractHtmlAttributeValue(string tagContent, string attributeName)
    {
        var searchFrom = 0;
        while (searchFrom < tagContent.Length)
        {
            var idx = tagContent.IndexOf(attributeName, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;

            var afterIdx = idx + attributeName.Length;
            var boundaryOk = idx == 0 || char.IsWhiteSpace(tagContent[idx - 1]);
            if (!boundaryOk)
            {
                searchFrom = afterIdx;
                continue;
            }

            var p = afterIdx;
            while (p < tagContent.Length && char.IsWhiteSpace(tagContent[p]))
                p++;
            if (p >= tagContent.Length || tagContent[p] != '=')
            {
                searchFrom = afterIdx;
                continue;
            }

            p++;
            while (p < tagContent.Length && char.IsWhiteSpace(tagContent[p]))
                p++;
            if (p >= tagContent.Length || (tagContent[p] != '"' && tagContent[p] != '\''))
            {
                searchFrom = afterIdx;
                continue;
            }

            var quote = tagContent[p];
            var valueStart = p + 1;
            var valueEnd = tagContent.IndexOf(quote, valueStart);
            if (valueEnd < 0)
                return null;

            return tagContent[valueStart..valueEnd];
        }

        return null;
    }

    private void ExecuteInsertCopiedCells()
    {
        if (_internalClipboard is not { } clip || SheetGrid.SelectedRange is not { } range)
            return;

        if (!TryShowCellShiftDialog(CellShiftDialogMode.Insert, out var choice))
            return;

        IWorkbookCommand CreateCommand()
        {
            var currentRange = SheetGrid.SelectedRange ?? range;
            // R110-insert-copied-cells-multiarea-1: forward clip.SourceAreas (mirrors the r108 fix
            // to the plain Ctrl+V path at ExecutePaste above) so the CF/DV carry inside
            // InsertCopiedCellsPlanner.CreateCommand's PasteCommandFactory call restricts itself to
            // the ACTUAL copied areas of a multi-area (Ctrl+click) source selection instead of
            // treating its whole bounding box -- including the untouched gap between disjoint
            // areas -- as copied.
            return InsertCopiedCellsPlanner.CreateCommand(
                _workbook,
                _currentSheetId,
                clip.SourceRange,
                clip.Cells,
                currentRange,
                choice,
                isCut: clip.IsCut,
                sourceAreas: clip.SourceAreas);
        }

        if (!TryExecuteRepeatableCommand(CreateCommand, "Insert Copied Cells", out _))
            return;

        var preserveClipboardVisual = ClipboardPastePlanner.ShouldPreserveClipboardVisualAfterPaste(clip.IsCut);
        _repeatPostAction = _ => CompletePasteSelection(clip.SourceRange, default, preserveClipboardVisual);
        CompletePasteSelection(clip.SourceRange, default, preserveClipboardVisual);
        if (clip.IsCut)
        {
            _internalClipboard = null;
            InvalidateOsClipboardAfterCutMove();
        }
        UpdateViewport();
        RefreshToolbar();
    }

    private void CompletePasteSelection(
        GridRange sourceRange,
        PasteSpecialOptions options,
        bool preserveClipboardVisual = false,
        bool expandToSelectedRange = false)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var pastedRows = options.Transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var pastedCols = options.Transpose ? sourceRange.RowCount : sourceRange.ColCount;
        if (expandToSelectedRange)
        {
            pastedRows = Math.Max(pastedRows, range.RowCount);
            pastedCols = Math.Max(pastedCols, range.ColCount);
        }

        var pastedEnd = new CellAddress(
            _currentSheetId,
            range.Start.Row + (uint)pastedRows - 1,
            range.Start.Col + (uint)pastedCols - 1);

        _selectionAnchor = range.Start;
        _selectionCursor = pastedEnd;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(range.Start, pastedEnd);
        ApplyClipboardVisualStateAfterInternalPaste(sourceRange, preserveClipboardVisual);
    }

    private void ApplyClipboardVisualStateAfterInternalPaste(GridRange sourceRange, bool preserveClipboardVisual)
    {
        if (preserveClipboardVisual)
        {
            SheetGrid.ClipboardRange = sourceRange;
            SheetGrid.ClipboardIsCut = false;
            return;
        }

        ClearClipboardVisualState();
    }

    private bool TryCreateCutMoveCommand(
        InternalClipboard clip,
        PasteMode mode,
        PasteSpecialOptions options,
        bool keepColumnWidths,
        CellAddress destination,
        out IWorkbookCommand command)
    {
        command = null!;
        if (!clip.IsCut || keepColumnWidths)
            return false;

        // MoveRangeCommand moves every cell in one contiguous rectangle. A multi-area Cut's
        // SourceRange is only the bounding box of the actually-copied areas (R49-render-multiarea-
        // selection-3-1); routing it through a single MoveRangeCommand would incorrectly move (and
        // clear) the "gap" cells between areas that were never selected. Fall back to the generic
        // copy-then-per-area-clear path below for that case.
        if (clip.SourceAreas is { Count: > 1 })
            return false;

        // Only the plain "Paste" gesture (no Paste Special mode/options) is a straight move in
        // Excel; Paste Special after a cut falls back to the legacy copy+clear behaviour below.
        if (mode != PasteMode.All || options != default)
            return false;

        // Grouped multi-sheet editing cannot be expressed as a single move (it would have to move
        // the same range on every grouped sheet at once), so fall back to the grouped copy+clear
        // path below whenever grouped editing is actually active across more than one sheet.
        var targetSheetIds = CurrentGroupedEditSheetIds();
        if (targetSheetIds.Count != 1)
            return false;

        // The paste destination must be the single active/grouped-edit sheet (it always is in
        // practice, since the destination comes from the currently displayed SheetGrid). The
        // SOURCE sheet, however, is allowed to differ from the destination sheet: MoveRangeCommand
        // fully supports a cross-sheet move (its isCrossSheet branch rewrites references across
        // sheets), so a Cut on one sheet followed by a Paste on another is exactly this gesture
        // and must not be downgraded to the copy+clear fallback (which never repoints other
        // formulas to follow the moved cells) -- matching WorkbookSession.cs's Avalonia-facing
        // equivalent, which already allows this.
        if (targetSheetIds[0] != destination.Sheet)
            return false;

        command = new MoveRangeCommand(clip.SourceRange.Start.Sheet, clip.SourceRange, destination);
        return true;
    }

    private void CompleteExternalPasteSelection(
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool expandToSelectedRange = false)
    {
        if (SheetGrid.SelectedRange is not { } range || rows.Count == 0)
            return;

        var pastedColCount = rows.Count == 0 ? 0 : rows.Max(row => row.Count);
        if (pastedColCount == 0)
            return;
        if (expandToSelectedRange)
            pastedColCount = Math.Max(pastedColCount, (int)range.ColCount);

        var pastedRowCount = expandToSelectedRange
            ? Math.Max(rows.Count, (int)range.RowCount)
            : rows.Count;

        var pastedEnd = new CellAddress(
            _currentSheetId,
            range.Start.Row + (uint)pastedRowCount - 1,
            range.Start.Col + (uint)pastedColCount - 1);

        _selectionAnchor = range.Start;
        _selectionCursor = pastedEnd;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(range.Start, pastedEnd);
        ClearClipboardVisualState();
    }

    private bool TryPasteClipboardImage(CellAddress anchor)
    {
        byte[] imageBytes;
        int pixelWidth;
        int pixelHeight;
        try
        {
            // R91-io-clipboard-image-formats-5-4: prefer an alpha-preserving raw "PNG" clipboard
            // format when the source app placed one (Chrome/Edge and many image editors place both a
            // flattened CF_DIB/CF_BITMAP entry AND a separate "PNG" entry with the real alpha channel
            // intact). WPF's Clipboard.GetImage()/ContainsImage() resolve exclusively to the
            // DIB/Bitmap entry, which has no alpha -- using it unconditionally silently bakes a solid
            // matte over a transparent-background PNG on every paste. Only fall back to GetImage()
            // (opaque) when no richer format is present.
            if (TryGetClipboardPngFormatBytes() is { } pngFormatBytes)
            {
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    new System.IO.MemoryStream(pngFormatBytes),
                    System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                imageBytes = pngFormatBytes;
                pixelWidth = frame.PixelWidth;
                pixelHeight = frame.PixelHeight;
            }
            else
            {
                if (!System.Windows.Clipboard.ContainsImage())
                    return false;

                var image = System.Windows.Clipboard.GetImage();
                if (image is null)
                    return false;

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var stream = new System.IO.MemoryStream();
                encoder.Save(stream);
                imageBytes = stream.ToArray();
                pixelWidth = image.PixelWidth;
                pixelHeight = image.PixelHeight;
            }
        }
        catch (Exception ex)
        {
            RecordDiagnosticEvent("clipboard_image_decode_failed", new Dictionary<string, string?>
            {
                ["reason"] = ex.GetType().Name
            });
            return false;
        }

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Paste Picture",
                sheetId =>
                {
                    var currentAnchor = SheetGrid.SelectedRange?.Start ?? anchor;
                    return ClipboardPictureService.CreateInsertCommand(
                        sheetId,
                        new CellAddress(sheetId, currentAnchor.Row, currentAnchor.Col),
                        imageBytes,
                        pixelWidth,
                        pixelHeight);
                }))
            return true;

        ClearClipboardVisualState();
        UpdateViewport();
        RefreshToolbar();
        return true;
    }

    private void ExecuteClearSelection()
    {
        // R121-model-drawing-delete-1: Delete on a currently-selected picture/text box/shape/chart
        // removes the OBJECT (matching Excel), not the contents of whatever cell range happens to be
        // selected underneath it.
        if (TryDeleteSelectedDrawingObject())
            return;

        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentSelectionRangesCommand(
                "Clear Contents",
                range,
                (sheetId, currentRange) => new ClearContentsCommand(sheetId, currentRange),
                out var outcome))
            return;

        // R54-render-copy-cut-marquee-4-1: Delete/Clear Contents on a still-active Copy/Cut
        // marquee must cancel it, matching Excel -- otherwise a later Paste would silently
        // move/copy the source range using its now-cleared (not the originally copied) contents.
        if (_internalClipboard is not null || SheetGrid.ClipboardRange is not null)
        {
            _internalClipboard = null;
            ClearClipboardVisualState();
        }

        UpdateViewport();
        if (SheetGrid.SelectedRange is { } selectedRange)
        {
            var activeCell = selectedRange.Start;
            FormulaBar.Text = FormatFormulaBarText(
                _workbook.GetSheet(_currentSheetId)?.GetCell(activeCell),
                activeCell);
        }
    }

    /// <summary>
    /// R75-commands-clear-delete-4-1: Backspace on a (possibly multi-cell) selection clears ONLY the
    /// active cell -- unlike Delete/Clear Contents (<see cref="ExecuteClearSelection"/>), which
    /// clears the whole selection. Matches Excel: Backspace is never a bulk-clear operation. Uses
    /// TryExecuteRepeatableGroupedSheetCommand (rather than
    /// TryExecuteRepeatableCurrentSelectionRangesCommand, which always resolves the actual
    /// multi-area SheetGrid selection) so only the single active cell is targeted, remapped per
    /// grouped sheet like the keyboard Insert/Delete Cells paths above.
    ///
    /// R76-meta-1: the active cell is <see cref="GridView.ActiveCell"/> (SheetGrid's tracked
    /// anchor), NOT the normalized top-left corner of the selection
    /// (<c>SelectedRange.Start</c>) -- those differ after an extended selection (e.g. Shift+click
    /// up/left of the anchor) or a Tab-within-selection. Using Start there cleared the wrong cell
    /// and left the true active cell untouched (data loss). Mirrors the correct pattern already
    /// used by GridView.cs/GridView.Rendering.Selection.cs, which prefer ActiveCell over
    /// SelectedRange.Start.
    /// </summary>
    private void ExecuteClearActiveCell()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Clear Contents",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var activeCell = SheetGrid.ActiveCell ?? currentRange.Start;
                    var activeCellRange = new GridRange(activeCell, activeCell);
                    return new ClearContentsCommand(sheetId, GroupedSheetRangePlanner.RemapRangeToSheet(activeCellRange, sheetId));
                },
                out var outcome))
            return;

        if (_internalClipboard is not null || SheetGrid.ClipboardRange is not null)
        {
            _internalClipboard = null;
            ClearClipboardVisualState();
        }

        UpdateViewport();
        if (SheetGrid.SelectedRange is { } selectedRange)
        {
            var activeCell = selectedRange.Start;
            FormulaBar.Text = FormatFormulaBarText(
                _workbook.GetSheet(_currentSheetId)?.GetCell(activeCell),
                activeCell);
        }
    }

    private void PasteSpecialBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_internalClipboard is null)
        {
            string text;
            try { text = System.Windows.Clipboard.GetText(); }
            catch { return; }
            if (string.IsNullOrEmpty(text)) return;
        }

        var dlg = new PasteSpecialDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var plan = PasteSpecialPlanner.CreatePlan(new PasteSpecialDialogSelection(
            dlg.Mode,
            dlg.Operation,
            dlg.SkipBlanks,
            dlg.Transpose,
            dlg.KeepColumnWidths,
            dlg.PasteLink));
        switch (plan.Action)
        {
            case PasteSpecialAction.ColumnWidths:
                ExecutePasteColumnWidthsOnly();
                return;
            case PasteSpecialAction.Comments:
                ExecutePasteComments(plan.Options.Transpose);
                return;
            case PasteSpecialAction.Validation:
                ExecutePasteValidation(plan.Options.Transpose);
                return;
            case PasteSpecialAction.Picture:
                ExecutePasteAsPicture(isLinkedPicture: false);
                return;
            case PasteSpecialAction.LinkedPicture:
                ExecutePasteAsPicture(isLinkedPicture: true);
                return;
            case PasteSpecialAction.Link:
                ExecutePasteLink(plan.Options.Transpose, plan.KeepColumnWidths);
                return;
            case PasteSpecialAction.ExternalText:
                ExecutePaste(plan.PasteMode, plan.Options, plan.KeepColumnWidths, externalTextAsText: true);
                return;
            default:
                ExecutePaste(plan.PasteMode, plan.Options, plan.KeepColumnWidths);
                return;
        }
    }

    private void ExecutePasteColumnWidthsOnly()
    {
        if (_internalClipboard is not { } clip || SheetGrid.SelectedRange is not { } range)
            return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Paste Column Widths",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    // R78-commands-paste-special-5-1: forward clip.SourceAreas so a multi-area
                    // (Ctrl+click) source's gap columns (never actually selected) are left untouched
                    // at the destination instead of being clobbered.
                    return new PasteColumnWidthsCommand(sheetId, clip.SourceRange, currentRange.Start.Col, currentRange.ColCount, clip.SourceAreas);
                },
                out var outcome))
            return;

        if (!outcome.Success)
            return;

        ApplyClipboardVisualStateAfterInternalPaste(
            clip.SourceRange,
            ClipboardPastePlanner.ShouldPreserveClipboardVisualAfterPaste(clip.IsCut));
        if (clip.IsCut)
            _internalClipboard = null;
        UpdateViewport();
        RefreshToolbar();
    }

    private void ExecutePasteComments(bool transpose)
    {
        if (_internalClipboard is not { } clip || SheetGrid.SelectedRange is not { } range)
            return;

        // R64-commands-paste-special-6-1: pass the full selected destination range (remapped per
        // grouped sheet), not just its top-left CellAddress, to the GridRange-tiling overload of
        // PasteCommentsCommand -- mirroring WorkbookSession.PasteCommentsFromClipboardAtActiveCell
        // (GetSinglePasteDestinationRange) -- so copying a comment/validation block and pasting into
        // a whole-multiple-sized selection tiles across the whole destination instead of only ever
        // filling the selection's top-left quadrant.
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Paste Comments",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var destinationRange = GroupedSheetRangePlanner.RemapRangeToSheet(currentRange, sheetId);
                    // R78-commands-paste-special-5-3: forward clip.SourceAreas so a multi-area
                    // (Ctrl+click) source's gap cells (never actually selected) don't leak a
                    // comment/note into the destination.
                    return new PasteCommentsCommand(
                        sheetId,
                        clip.SourceRange,
                        destinationRange,
                        transpose,
                        clip.SourceAreas);
                },
                out var outcome))
            return;

        if (!outcome.Success)
            return;

        CompletePasteSelection(
            clip.SourceRange,
            new PasteSpecialOptions(Transpose: transpose),
            ClipboardPastePlanner.ShouldPreserveClipboardVisualAfterPaste(clip.IsCut),
            expandToSelectedRange: true);
        if (clip.IsCut)
            _internalClipboard = null;
        UpdateViewport();
        RefreshToolbar();
    }

    private void ExecutePasteValidation(bool transpose)
    {
        if (_internalClipboard is not { } clip || SheetGrid.SelectedRange is not { } range)
            return;

        // R64-commands-paste-special-6-1: same destination-range tiling fix as
        // ExecutePasteComments above, applied to the GridRange-tiling overload of
        // PasteDataValidationCommand.
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Paste Validation",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var destinationRange = GroupedSheetRangePlanner.RemapRangeToSheet(currentRange, sheetId);
                    // R78-commands-paste-special-5-4: forward clip.SourceAreas so a multi-area
                    // (Ctrl+click) source's gap cells (never actually selected) don't leak a
                    // validation rule into the destination.
                    return new PasteDataValidationCommand(
                        sheetId,
                        clip.SourceRange,
                        destinationRange,
                        transpose,
                        clip.SourceAreas);
                },
                out var outcome))
            return;

        if (!outcome.Success)
            return;

        CompletePasteSelection(
            clip.SourceRange,
            new PasteSpecialOptions(Transpose: transpose),
            ClipboardPastePlanner.ShouldPreserveClipboardVisualAfterPaste(clip.IsCut),
            expandToSelectedRange: true);
        if (clip.IsCut)
            _internalClipboard = null;
        UpdateViewport();
        RefreshToolbar();
    }

    private void ExecutePasteAsPicture(bool isLinkedPicture)
    {
        if (_internalClipboard is not { } clip || SheetGrid.SelectedRange is not { } range)
            return;

        var sourceSheet = isLinkedPicture
            ? _workbook.GetSheet(clip.SourceRange.Start.Sheet)
            : null;
        if (isLinkedPicture && sourceSheet is null)
            return;

        var sourceCells = clip.PictureCells;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Paste Picture",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var destination = GroupedSheetRangePlanner.RemapRangeToSheet(currentRange, sheetId).Start;
                    return new PasteRangeAsPictureCommand(
                        sheetId,
                        clip.SourceRange,
                        sourceCells,
                        destination,
                        isLinkedPicture,
                        sourceSheet?.Name);
                },
                out var outcome))
            return;

        if (!outcome.Success)
            return;

        var preserveClipboardVisual = ClipboardPastePlanner.ShouldPreserveClipboardVisualAfterPaste(clip.IsCut);
        _repeatPostAction = _ => ApplyClipboardVisualStateAfterInternalPaste(clip.SourceRange, preserveClipboardVisual);
        ApplyClipboardVisualStateAfterInternalPaste(clip.SourceRange, preserveClipboardVisual);
        if (clip.IsCut)
            _internalClipboard = null;
        UpdateViewport();
        RefreshToolbar();
    }

    private void ExecutePasteLink(bool transpose, bool keepColumnWidths = false)
    {
        if (_internalClipboard is not { } clip || SheetGrid.SelectedRange is not { } range)
            return;

        var sourceSheet = _workbook.GetSheet(clip.SourceRange.Start.Sheet);
        if (sourceSheet is null)
            return;

        IWorkbookCommand CreatePasteLinkCommand()
        {
            var currentRange = SheetGrid.SelectedRange ?? range;
            // R64-commands-paste-special-6-2: pass the full selected destination range to the
            // tiling overload of PasteLinkService.CreateLinkedCells (not the 4-arg overload, which
            // forwards destinationRange: null) -- mirroring
            // WorkbookSession.PasteLinkFromClipboardAtActiveCell/CreatePasteLinkCommand -- so a
            // copied source tiles its linked formulas across the whole destination selection
            // instead of only ever filling the source range's own footprint.
            // R78-commands-paste-special-5-2: forward clip.SourceAreas so a multi-area (Ctrl+click)
            // source's gap cells (never actually selected) don't get planted with a spurious link
            // formula at the destination.
            var linkedCells = PasteLinkService.CreateLinkedCells(
                clip.SourceRange,
                currentRange.Start,
                currentRange,
                sourceSheet.Name,
                transpose,
                clip.SourceAreas);
            var targetSheetIds = CurrentGroupedEditSheetIds();
            IWorkbookCommand linkCommand = targetSheetIds.Count > 1
                ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, linkedCells)
                : new EditCellsCommand(_currentSheetId, linkedCells);
            return keepColumnWidths
                ? new CompositeWorkbookCommand(
                    "Paste Link",
                    [
                        linkCommand,
                        new PasteColumnWidthsCommand(_currentSheetId, clip.SourceRange, currentRange.Start.Col, currentRange.ColCount, clip.SourceAreas)
                    ])
                : linkCommand;
        }

        if (!TryExecuteRepeatableCommand(CreatePasteLinkCommand, "Paste Link", out _))
            return;

        var preserveClipboardVisual = ClipboardPastePlanner.ShouldPreserveClipboardVisualAfterPaste(clip.IsCut);
        _repeatPostAction = _ => CompletePasteSelection(clip.SourceRange, new PasteSpecialOptions(Transpose: transpose), preserveClipboardVisual);
        CompletePasteSelection(clip.SourceRange, new PasteSpecialOptions(Transpose: transpose), preserveClipboardVisual);
        if (clip.IsCut)
            _internalClipboard = null;
        UpdateViewport();
        RefreshToolbar();
    }

    /// <summary>
    /// Bumps the navigation-cache revision (sparklines / status-bar stats) when the workbook is
    /// in a manual calculation mode. Paste/Paste Link/Insert Copied Cells all write cell values
    /// (or new formulas) immediately regardless of calculation mode -- Excel reflects that in the
    /// grid right away, only formula recalculation is deferred by Manual mode. But
    /// <see cref="RecalculateIfAutomatic"/> is a no-op outside Automatic/AutomaticExceptDataTables
    /// mode, so without this it never bumps <c>_navigationCacheRevision</c>, and
    /// SparklineValueCache/StatusBarStatsCache (both keyed on that revision) keep returning their
    /// pre-paste cached result until an unrelated command happens to bump the revision. Mirrors
    /// the Goal Seek fix in MainWindow.DataCommands.cs.
    /// </summary>
    private void InvalidateNavigationCachesIfManual()
    {
        if (_workbook.CalculationMode is not (WorkbookCalculationMode.Automatic or WorkbookCalculationMode.AutomaticExceptDataTables))
        {
            InvalidateNavigationCaches();
        }
    }

    // ── HTML clipboard payload (CF_HTML) ─────────────────────────────────────
    //
    // Real Excel places CF_HTML (and RTF) on the clipboard alongside plain text so that
    // formatting-aware destinations (Word, Outlook, browsers, LibreOffice Calc) preserve bold,
    // fill colors, alignment, borders, and merged cells instead of receiving flattened TSV text.
    // This mirrors that for the write side (M7). The CSS mapping intentionally matches
    // FreeX.Core.IO.HtmlTableWriter's conventions (bold/italic/underline, effective font name,
    // resolved font/fill color, horizontal alignment, per-edge borders) so pasting into FreeX's
    // own HTML importer — or re-exporting to .html — sees consistent styling either way.
    //
    // Read-side: ExecutePaste's external-clipboard fallback (TryGetClipboardHtml +
    // TryParseHtmlClipboardTableRows below) recovers the pasted table's actual <tr>/<td> row/column
    // structure from a foreign app's CF_HTML payload when present, so a source cell whose text spans
    // multiple lines doesn't get misread as a row break by the plain-text splitter
    // (R39-io-external-clipboard-2-3). Full per-cell STYLE reconstruction (fonts/fills/borders/merges
    // from the pasted HTML, mirroring FreeX.Core.IO.HtmlTableReader used for whole-file HTML import)
    // is a materially larger feature and remains a follow-up; plain-text/plain-image paste continues
    // to work unchanged via the existing TryGetClipboardText/TryClipboardContainsImage paths when no
    // HTML table is found.

    /// <summary>
    /// Builds a CF_HTML-wrapped clipboard payload (header + HTML fragment) for <paramref name="range"/>,
    /// or <c>null</c> if the range is empty/invalid. Returns the full string ready for
    /// <see cref="System.Windows.DataObject.SetData(string, object)"/> with
    /// <see cref="System.Windows.DataFormats.Html"/> — WPF does not auto-wrap CF_HTML, the header
    /// with byte offsets must be supplied by the caller.
    /// </summary>
    private static string? BuildHtmlClipboardFragment(
        ViewportModel viewport, Sheet? sheet, GridRange range, WorkbookTheme theme) =>
        ClipboardHtmlSerializer.Serialize(viewport, sheet, range, theme)?.CfHtml;

    /// <summary>
    /// R91-io-clipboard-image-formats-5-3: renders a simple bordered-grid Bitmap of a copied range's
    /// display text so a normal copy always carries a picture clipboard flavor (see ExecuteCopy) --
    /// the smallest correct stand-in for a full "as shown on screen" render, deliberately independent
    /// of the shared print/grid drawing pipeline. Returns null (never throws) for anything that would
    /// make an unreasonable bitmap: empty content, or a huge cell count.
    /// </summary>
    private static System.Windows.Media.Imaging.BitmapSource? TryRenderClipboardRangeBitmap(string[][] rows)
    {
        const double cellWidth = 80;
        const double cellHeight = 20;
        const int maxCells = 2000;

        try
        {
            if (rows.Length == 0)
                return null;

            var colCount = rows.Max(row => row.Length);
            if (colCount == 0)
                return null;

            var rowCount = rows.Length;
            if ((long)rowCount * colCount > maxCells)
                return null;

            var width = colCount * cellWidth;
            var height = rowCount * cellHeight;

            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, width, height));

                var gridPen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.LightGray, 1);
                var typeface = new System.Windows.Media.Typeface("Segoe UI");
                for (var r = 0; r < rowCount; r++)
                {
                    var row = rows[r];
                    for (var c = 0; c < colCount; c++)
                    {
                        var cellRect = new Rect(c * cellWidth, r * cellHeight, cellWidth, cellHeight);
                        dc.DrawRectangle(null, gridPen, cellRect);

                        var cellText = c < row.Length ? row[c] : string.Empty;
                        if (string.IsNullOrEmpty(cellText))
                            continue;

                        var formatted = new System.Windows.Media.FormattedText(
                            cellText,
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            12,
                            System.Windows.Media.Brushes.Black,
                            1.0)
                        {
                            MaxTextWidth = Math.Max(1, cellWidth - 4),
                            MaxTextHeight = Math.Max(1, cellHeight - 2),
                            Trimming = TextTrimming.CharacterEllipsis
                        };
                        dc.DrawText(formatted, new Point(cellRect.Left + 2, cellRect.Top + 1));
                    }
                }
            }

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(width)),
                Math.Max(1, (int)Math.Ceiling(height)),
                96,
                96,
                System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            // Best-effort extra clipboard flavor -- never let a rendering hiccup fail the copy itself.
            return null;
        }
    }
}
