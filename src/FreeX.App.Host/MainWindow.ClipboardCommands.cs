using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using FreeX.App.Presentation.Editing;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
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
        IReadOnlyList<GridRange>? SourceAreas = null);
    private InternalClipboard? _internalClipboard;

    private void CancelCopyAndTransientModes()
    {
        ClearClipboardVisualState();
        _internalClipboard = null;
        CancelFormatPainter();
        _borderDrawMode = BorderDrawMode.None;
        SetSelectionMode(ExcelSelectionMode.Normal);
        SetEndMode(false);
    }

    private void ClearClipboardVisualState()
    {
        SheetGrid.ClipboardRange = null;
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
        var boundingRange = GetSelectionBoundingRange(areas, range);

        // P41: SheetGrid.Viewport only materializes the on-screen scroll position (see
        // ViewportService.Metrics BuildFrozenAwareRowMetrics, which stops once it has covered the
        // visible height/width). Serializing/HTML-rendering directly off that viewport truncates
        // any part of the copied range that falls outside the current scroll position to blank —
        // both for the plain-text/CF_HTML clipboard payload placed on the OS clipboard for external
        // paste, and would silently corrupt internal same-instance paste too if not for the
        // clip.Cells fallback captured further below. Build a viewport request sized to the actual
        // copied range instead, so external copy/paste (and CF_HTML) always reflects the full
        // selection regardless of what is currently scrolled into view.
        var fullRangeViewport = BuildFullRangeViewportForClipboard(boundingRange) ?? viewport;

        var text = ClipboardSerializer.Serialize(fullRangeViewport, boundingRange);
        var sheetForHtml = _workbook.GetSheet(_currentSheetId);
        try
        {
            // Place plain text AND an HTML table fragment (CF_HTML) on the OS clipboard together,
            // matching real Excel: destination apps that understand HTML (Word, Outlook, browsers,
            // LibreOffice Calc) pick the richer format and preserve bold/fill/merges/number-format
            // display text, while anything HTML-unaware still gets the existing plain TSV text (M7).
            var data = new DataObject();
            data.SetText(text);
            var html = BuildHtmlClipboardFragment(fullRangeViewport, sheetForHtml, boundingRange, _workbook.Theme);
            if (!string.IsNullOrEmpty(html))
                data.SetData(System.Windows.DataFormats.Html, html);

            // R57-services-clipboard-formats-5-3: real Excel places a comma-delimited "CSV" clipboard
            // format alongside Text/Unicode Text/HTML on every cell-range copy, so a destination that
            // specifically enumerates for CSV (skipping plain Text) still gets a payload. Re-parse the
            // already-built TSV/newline `text` (same field values/escaping semantics as ClipboardSerializer
            // production, just re-delimited) and re-emit it RFC4180-quoted with commas.
            var csv = BuildCsvClipboardText(text);
            if (!string.IsNullOrEmpty(csv))
                data.SetData(System.Windows.DataFormats.CommaSeparatedValue, csv);

            System.Windows.Clipboard.SetDataObject(data, copy: true);
        }
        catch
        {
            // Clipboard may be locked by another process — fall back to plain text only.
            try { System.Windows.Clipboard.SetText(text); }
            catch { /* clipboard may be locked */ }
        }

        // Show marching ants around the copied range. GridView.ClipboardRange only supports one
        // rectangle (no multi-area marching-ants rendering), so a disjoint selection's visual
        // indicator is the bounding box -- a pre-existing GridView (App.UI) limitation outside this
        // file's scope; it does not affect what actually gets copied/pasted below.
        SheetGrid.ClipboardRange = boundingRange;
        SheetGrid.ClipboardIsCut = isCut;

        // Capture raw cells (including formulas) for paste formula adjustment -- from EVERY
        // selected area, not just the active one, de-duplicating in case areas ever overlap.
        var sheet = _workbook.GetSheet(_currentSheetId);
        var clipCells = new List<(CellAddress, Cell)>();
        var seenAddresses = new HashSet<CellAddress>();
        foreach (var area in areas)
        {
            for (uint r = area.Start.Row; r <= area.End.Row; r++)
            {
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
        var pictureCells = CapturePictureCells(fullRangeViewport, sheet, boundingRange);
        _internalClipboard = new InternalClipboard(
            boundingRange,
            clipCells,
            pictureCells,
            text,
            isCut,
            areas.Count > 1 ? areas : null);
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
            currentClipboardText = TryGetClipboardText(out var clipboardReadFailed);
            currentClipboardTextRead = true;
            var pastePlan = ClipboardPastePlanner.PlanPaste(clip.Text, currentClipboardText, clipboardReadFailed);
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
                        var sheetPasteCommand = PasteCommandFactory.CreateInternalPasteCommand(
                            _workbook,
                            sheetId,
                            clip.SourceRange,
                            clip.Cells,
                            sheetDestinationRange,
                            ClipboardPastePlanner.ToCorePasteMode(mode),
                            options);
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

                var pasteOutcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreatePasteCommand);
                if (!pasteOutcome.Success)
                {
                    ShowCommandError(pasteOutcome, title);
                    return;
                }

                var preserveClipboardVisual = ClipboardPastePlanner.ShouldPreserveClipboardVisualAfterPaste(clip.IsCut);
                _repeatPostAction = _ =>
                {
                    CompletePasteSelection(
                        clip.SourceRange,
                        options,
                        preserveClipboardVisual,
                        expandToSelectedRange: expandPasteToSelectedRange);
                    if (clip.IsCut)
                        _internalClipboard = null;
                };
                if (mode != PasteMode.Formats)
                {
                    RecalculateIfAutomatic(pasteOutcome.AffectedCells ?? []);
                    InvalidateNavigationCachesIfManual();
                }

                CompletePasteSelection(
                    clip.SourceRange,
                    options,
                    preserveClipboardVisual,
                    expandToSelectedRange: expandPasteToSelectedRange);
                if (clip.IsCut)
                    _internalClipboard = null;
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

        var fallbackOutcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateExternalPasteCommand);
        if (!fallbackOutcome.Success)
        {
            ShowCommandError(fallbackOutcome, "Paste");
            return;
        }

        _repeatPostAction = _ => CompleteExternalPasteSelection(capturedRows, expandToSelectedRange: true);
        RecalculateIfAutomatic(fallbackOutcome.AffectedCells ?? []);
        InvalidateNavigationCachesIfManual();

        CompleteExternalPasteSelection(capturedRows, expandToSelectedRange: true);
        UpdateViewport();
        RefreshToolbar();
    }

    private static string? TryGetClipboardText() => TryGetClipboardText(out _);

    /// <summary>
    /// Reads the OS clipboard text, distinguishing "read failed" (clipboard locked by another
    /// process) from "read succeeded but empty/non-text" — the paste planner must skip the paste
    /// on failure instead of falling back to a stale internal-clipboard paste (review P1).
    /// </summary>
    private static string? TryGetClipboardText(out bool readFailed)
    {
        try
        {
            readFailed = false;
            return System.Windows.Clipboard.GetText();
        }
        catch
        {
            readFailed = true;
            return null;
        }
    }

    private static bool TryClipboardContainsImage()
    {
        try { return System.Windows.Clipboard.ContainsImage(); }
        catch { return false; }
    }

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
    /// merged-header columns aligned with their data (R57-services-clipboard-formats-5-2).</summary>
    private readonly record struct HtmlCellSpan(string InnerHtml, int ColSpan, int RowSpan);

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
                int closeStart = FindMatchingHtmlClose(rowInner, tagEnd + 1, name);
                string inner = closeStart < 0 ? rowInner[(tagEnd + 1)..] : rowInner[(tagEnd + 1)..closeStart];
                yield return new HtmlCellSpan(inner, colSpan, rowSpan);
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

            return int.TryParse(
                tagContent[digitsStart..p],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value) && value > 0
                ? value
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
    /// kept WITHIN the cell's own text (never a row separator) -- decodes entities, and trims.</summary>
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
                    sb.Append('\n');
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

    private void ExecuteInsertCopiedCells()
    {
        if (_internalClipboard is not { } clip || SheetGrid.SelectedRange is not { } range)
            return;

        if (!TryShowCellShiftDialog(CellShiftDialogMode.Insert, out var choice))
            return;

        IWorkbookCommand CreateCommand()
        {
            var currentRange = SheetGrid.SelectedRange ?? range;
            return InsertCopiedCellsPlanner.CreateCommand(
                _workbook,
                _currentSheetId,
                clip.SourceRange,
                clip.Cells,
                currentRange,
                choice,
                isCut: clip.IsCut);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Insert Copied Cells");
            return;
        }

        var preserveClipboardVisual = ClipboardPastePlanner.ShouldPreserveClipboardVisualAfterPaste(clip.IsCut);
        _repeatPostAction = _ => CompletePasteSelection(clip.SourceRange, default, preserveClipboardVisual);
        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        InvalidateNavigationCachesIfManual();
        CompletePasteSelection(clip.SourceRange, default, preserveClipboardVisual);
        if (clip.IsCut)
            _internalClipboard = null;
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

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
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
                    return new PasteColumnWidthsCommand(sheetId, clip.SourceRange, currentRange.Start.Col, currentRange.ColCount);
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

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Paste Comments",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return new PasteCommentsCommand(
                        sheetId,
                        clip.SourceRange,
                        new CellAddress(sheetId, currentRange.Start.Row, currentRange.Start.Col),
                        transpose);
                },
                out var outcome))
            return;

        if (!outcome.Success)
            return;

        CompletePasteSelection(
            clip.SourceRange,
            new PasteSpecialOptions(Transpose: transpose),
            ClipboardPastePlanner.ShouldPreserveClipboardVisualAfterPaste(clip.IsCut));
        if (clip.IsCut)
            _internalClipboard = null;
        UpdateViewport();
        RefreshToolbar();
    }

    private void ExecutePasteValidation(bool transpose)
    {
        if (_internalClipboard is not { } clip || SheetGrid.SelectedRange is not { } range)
            return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Paste Validation",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return new PasteDataValidationCommand(
                        sheetId,
                        clip.SourceRange,
                        new CellAddress(sheetId, currentRange.Start.Row, currentRange.Start.Col),
                        transpose);
                },
                out var outcome))
            return;

        if (!outcome.Success)
            return;

        CompletePasteSelection(
            clip.SourceRange,
            new PasteSpecialOptions(Transpose: transpose),
            ClipboardPastePlanner.ShouldPreserveClipboardVisualAfterPaste(clip.IsCut));
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
            var linkedCells = PasteLinkService.CreateLinkedCells(
                clip.SourceRange,
                currentRange.Start,
                sourceSheet.Name,
                transpose);
            var targetSheetIds = CurrentGroupedEditSheetIds();
            IWorkbookCommand linkCommand = targetSheetIds.Count > 1
                ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, linkedCells)
                : new EditCellsCommand(_currentSheetId, linkedCells);
            return keepColumnWidths
                ? new CompositeWorkbookCommand(
                    "Paste Link",
                    [
                        linkCommand,
                        new PasteColumnWidthsCommand(_currentSheetId, clip.SourceRange, currentRange.Start.Col, currentRange.ColCount)
                    ])
                : linkCommand;
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreatePasteLinkCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Paste Link");
            return;
        }

        var preserveClipboardVisual = ClipboardPastePlanner.ShouldPreserveClipboardVisualAfterPaste(clip.IsCut);
        _repeatPostAction = _ => CompletePasteSelection(clip.SourceRange, new PasteSpecialOptions(Transpose: transpose), preserveClipboardVisual);
        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        InvalidateNavigationCachesIfManual();
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
    /// R57-services-clipboard-formats-5-3: re-delimits the tab/CRLF-separated <paramref name="tsvText"/>
    /// (as produced by <see cref="ClipboardSerializer.Serialize"/>) into RFC4180-quoted comma-separated
    /// text, for placing on the "CSV" clipboard format alongside Text/HTML. Re-parses via
    /// <see cref="ClipboardSerializer.Deserialize"/> (the same reader ExecutePaste's external-clipboard
    /// fallback already relies on) rather than re-implementing TSV parsing here.
    /// </summary>
    private static string BuildCsvClipboardText(string tsvText)
    {
        if (string.IsNullOrEmpty(tsvText))
            return string.Empty;

        var rows = ClipboardSerializer.Deserialize(tsvText);
        var sb = new StringBuilder(tsvText.Length + 16);
        for (var r = 0; r < rows.Length; r++)
        {
            if (r > 0)
                sb.Append("\r\n");

            var row = rows[r];
            for (var c = 0; c < row.Length; c++)
            {
                if (c > 0)
                    sb.Append(',');

                AppendCsvField(sb, row[c]);
            }
        }

        return sb.ToString();
    }

    private static void AppendCsvField(StringBuilder sb, string field)
    {
        var requiresQuoting = false;
        foreach (var ch in field)
        {
            if (ch is ',' or '"' or '\r' or '\n')
            {
                requiresQuoting = true;
                break;
            }
        }

        if (!requiresQuoting)
        {
            sb.Append(field);
            return;
        }

        sb.Append('"');
        foreach (var ch in field)
        {
            if (ch == '"')
                sb.Append("\"\"");
            else
                sb.Append(ch);
        }

        sb.Append('"');
    }
}
