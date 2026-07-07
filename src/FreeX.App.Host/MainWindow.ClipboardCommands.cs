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
    private record InternalClipboard(
        GridRange SourceRange,
        List<(CellAddress Source, Cell Cell)> Cells,
        List<(CellAddress Source, PictureCellSnapshot Snapshot)> PictureCells,
        string Text,
        bool IsCut = false);
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

        // P41: SheetGrid.Viewport only materializes the on-screen scroll position (see
        // ViewportService.Metrics BuildFrozenAwareRowMetrics, which stops once it has covered the
        // visible height/width). Serializing/HTML-rendering directly off that viewport truncates
        // any part of the copied range that falls outside the current scroll position to blank —
        // both for the plain-text/CF_HTML clipboard payload placed on the OS clipboard for external
        // paste, and would silently corrupt internal same-instance paste too if not for the
        // clip.Cells fallback captured further below. Build a viewport request sized to the actual
        // copied range instead, so external copy/paste (and CF_HTML) always reflects the full
        // selection regardless of what is currently scrolled into view.
        var fullRangeViewport = BuildFullRangeViewportForClipboard(range) ?? viewport;

        var text = ClipboardSerializer.Serialize(fullRangeViewport, range);
        var sheetForHtml = _workbook.GetSheet(_currentSheetId);
        try
        {
            // Place plain text AND an HTML table fragment (CF_HTML) on the OS clipboard together,
            // matching real Excel: destination apps that understand HTML (Word, Outlook, browsers,
            // LibreOffice Calc) pick the richer format and preserve bold/fill/merges/number-format
            // display text, while anything HTML-unaware still gets the existing plain TSV text (M7).
            var data = new DataObject();
            data.SetText(text);
            var html = BuildHtmlClipboardFragment(fullRangeViewport, sheetForHtml, range, _workbook.Theme);
            if (!string.IsNullOrEmpty(html))
                data.SetData(System.Windows.DataFormats.Html, html);
            System.Windows.Clipboard.SetDataObject(data, copy: true);
        }
        catch
        {
            // Clipboard may be locked by another process — fall back to plain text only.
            try { System.Windows.Clipboard.SetText(text); }
            catch { /* clipboard may be locked */ }
        }

        // Show marching ants around the copied range
        SheetGrid.ClipboardRange = range;
        SheetGrid.ClipboardIsCut = isCut;

        // Capture raw cells (including formulas) for paste formula adjustment
        var sheet = _workbook.GetSheet(_currentSheetId);
        var clipCells = new List<(CellAddress, Cell)>();
        var pictureCells = CapturePictureCells(fullRangeViewport, sheet, range);
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
            {
                var addr = new CellAddress(_currentSheetId, r, c);
                var cell = sheet?.GetCell(r, c);
                clipCells.Add((addr, cell?.Clone() ?? Cell.FromValue(BlankValue.Instance)));
            }
        }
        _internalClipboard = new InternalClipboard(range, clipCells, pictureCells, text, isCut);
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

        // If we have an internal clipboard (copied from within this app), use it with formula adjustment
        if (_internalClipboard is { } clip)
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
                                    new PasteColumnWidthsCommand(sheetId, clip.SourceRange, sheetDestinationRange.Start.Col)
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
                        command = new CompositeWorkbookCommand(
                            "Cut and Paste",
                            [
                                command,
                                new ClearContentsCommand(clip.SourceRange.Start.Sheet, clip.SourceRange)
                            ]);
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
                    RecalculateIfAutomatic(pasteOutcome.AffectedCells ?? []);

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

        // Fallback: external clipboard (plain text)
        if (string.IsNullOrEmpty(text)) return;

        var rows = ClipboardSerializer.Deserialize(text);
        if (rows.Length == 0 || rows.All(r => r.Length == 0)) return;
        var capturedRows = rows.Select(row => (IReadOnlyList<string>)row).ToList();

        IWorkbookCommand CreateExternalPasteCommand()
        {
            var currentRange = SheetGrid.SelectedRange ?? range;
            return PasteCommandFactory.CreateExternalTextPasteCommand(
                _currentSheetId,
                currentRange,
                capturedRows,
                preserveText: externalTextAsText);
        }

        var fallbackOutcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateExternalPasteCommand);
        if (!fallbackOutcome.Success)
        {
            ShowCommandError(fallbackOutcome, "Paste");
            return;
        }

        _repeatPostAction = _ => CompleteExternalPasteSelection(capturedRows, expandToSelectedRange: true);
        RecalculateIfAutomatic(fallbackOutcome.AffectedCells ?? []);

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
                choice);
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

        // Only the plain "Paste" gesture (no Paste Special mode/options) is a straight move in
        // Excel; Paste Special after a cut falls back to the legacy copy+clear behaviour below.
        if (mode != PasteMode.All || options != default)
            return false;

        // MoveRangeCommand only supports a same-sheet move; grouped multi-sheet editing cannot be
        // expressed as a single move, so fall back to the grouped copy+clear path below.
        var targetSheetIds = CurrentGroupedEditSheetIds();
        if (targetSheetIds.Count != 1 || targetSheetIds[0] != clip.SourceRange.Start.Sheet)
            return false;

        if (clip.SourceRange.Start.Sheet != _currentSheetId || destination.Sheet != _currentSheetId)
            return false;

        command = new MoveRangeCommand(_currentSheetId, clip.SourceRange, destination);
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
                    return new PasteColumnWidthsCommand(sheetId, clip.SourceRange, currentRange.Start.Col);
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
                        new PasteColumnWidthsCommand(_currentSheetId, clip.SourceRange, currentRange.Start.Col)
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
        CompletePasteSelection(clip.SourceRange, new PasteSpecialOptions(Transpose: transpose), preserveClipboardVisual);
        if (clip.IsCut)
            _internalClipboard = null;
        UpdateViewport();
        RefreshToolbar();
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
    // Read-side (importing a foreign app's CF_HTML/RTF payload back into styled cells) is NOT
    // implemented here — that is a materially larger feature (HTML table parsing + per-cell style
    // reconstruction) and is left for a follow-up; plain-text/plain-image paste continues to work
    // unchanged via the existing TryGetClipboardText/TryClipboardContainsImage paths.

    /// <summary>
    /// Builds a CF_HTML-wrapped clipboard payload (header + HTML fragment) for <paramref name="range"/>,
    /// or <c>null</c> if the range is empty/invalid. Returns the full string ready for
    /// <see cref="System.Windows.DataObject.SetData(string, object)"/> with
    /// <see cref="System.Windows.DataFormats.Html"/> — WPF does not auto-wrap CF_HTML, the header
    /// with byte offsets must be supplied by the caller.
    /// </summary>
    private static string? BuildHtmlClipboardFragment(
        ViewportModel viewport, Sheet? sheet, GridRange range, WorkbookTheme theme)
    {
        if (range.RowCount == 0 || range.ColCount == 0)
            return null;

        var cellLookup = new Dictionary<(uint Row, uint Col), DisplayCell>(viewport.Cells.Count);
        foreach (var cell in viewport.Cells)
            cellLookup[(cell.Row, cell.Col)] = cell;

        // Map merge-region anchor -> region, and mark covered (non-anchor) cells to skip, exactly
        // as HtmlTableWriter does for full-sheet HTML export, so a copied merged range keeps its
        // rowspan/colspan on paste into another app instead of splitting back into single cells.
        var anchors = new Dictionary<(uint, uint), GridRange>();
        var covered = new HashSet<(uint, uint)>();
        if (sheet is not null)
        {
            foreach (var region in sheet.MergedRegions)
            {
                if (!RangesOverlap(region, range))
                    continue;

                anchors[(region.Start.Row, region.Start.Col)] = region;
                foreach (var addr in region.AllCells())
                {
                    if (addr.Row != region.Start.Row || addr.Col != region.Start.Col)
                        covered.Add((addr.Row, addr.Col));
                }
            }
        }

        var body = new StringBuilder();
        body.Append("<table border=\"1\" cellspacing=\"0\" style=\"border-collapse:collapse\">");
        for (var r = range.Start.Row; r <= range.End.Row; r++)
        {
            body.Append("<tr>");
            for (var c = range.Start.Col; c <= range.End.Col; c++)
            {
                if (covered.Contains((r, c)))
                    continue;

                var spanAttrs = "";
                if (anchors.TryGetValue((r, c), out var region))
                {
                    var colspan = Math.Min(region.ColCount, range.End.Col - c + 1);
                    var rowspan = Math.Min(region.RowCount, range.End.Row - r + 1);
                    if (colspan > 1) spanAttrs += $" colspan=\"{colspan}\"";
                    if (rowspan > 1) spanAttrs += $" rowspan=\"{rowspan}\"";
                }

                cellLookup.TryGetValue((r, c), out var displayCell);
                var css = displayCell.Style is { } cellStyle ? BuildCellCss(cellStyle, theme) : "";
                var styleAttr = css.Length > 0 ? $" style=\"{css}\"" : "";
                var display = EscapeHtml(displayCell.DisplayText ?? "");
                body.Append($"<td{spanAttrs}{styleAttr}>{display}</td>");
            }
            body.Append("</tr>");
        }
        body.Append("</table>");

        return WrapAsCfHtml(body.ToString());
    }

    private static bool RangesOverlap(GridRange a, GridRange b) =>
        a.Start.Row <= b.End.Row && a.End.Row >= b.Start.Row &&
        a.Start.Col <= b.End.Col && a.End.Col >= b.Start.Col;

    private static string BuildCellCss(CellStyle style, WorkbookTheme theme)
    {
        var sb = new StringBuilder();

        if (style.Bold) sb.Append("font-weight:bold;");
        if (style.Italic) sb.Append("font-style:italic;");
        if (style.Underline || style.DoubleUnderline) sb.Append("text-decoration:underline;");
        if (style.Strikethrough) sb.Append("text-decoration:line-through;");

        var fontName = style.ResolveEffectiveFontName(theme);
        if (!string.Equals(fontName, "Calibri", StringComparison.Ordinal))
            sb.Append($"font-family:'{fontName.Replace("'", "", StringComparison.Ordinal)}';");
        if (Math.Abs(style.FontSize - 11) > 0.001)
            sb.Append($"font-size:{style.FontSize.ToString("0.##", CultureInfo.InvariantCulture)}pt;");

        var fontColor = style.ResolveFontColor(theme);
        if (!fontColor.IsBlack)
            sb.Append($"color:{HexColor(fontColor)};");

        var fill = style.ResolveFillColor(theme);
        if (fill is { } f)
            sb.Append($"background-color:{HexColor(f)};");

        var align = style.HorizontalAlignment switch
        {
            FreeX.Core.Model.HorizontalAlignment.Left => "left",
            FreeX.Core.Model.HorizontalAlignment.Center => "center",
            FreeX.Core.Model.HorizontalAlignment.Right => "right",
            FreeX.Core.Model.HorizontalAlignment.Justify => "justify",
            _ => null,
        };
        if (align is not null)
            sb.Append($"text-align:{align};");

        AppendBorderCss(sb, "top", style.BorderTop);
        AppendBorderCss(sb, "right", style.BorderRight);
        AppendBorderCss(sb, "bottom", style.BorderBottom);
        AppendBorderCss(sb, "left", style.BorderLeft);

        return sb.ToString();
    }

    private static void AppendBorderCss(StringBuilder sb, string edge, CellBorder border)
    {
        if (border.Style == BorderStyle.None)
            return;

        var (width, line) = border.Style switch
        {
            BorderStyle.Thin => ("1px", "solid"),
            BorderStyle.Medium => ("2px", "solid"),
            BorderStyle.Thick => ("3px", "solid"),
            BorderStyle.Dashed => ("1px", "dashed"),
            BorderStyle.Dotted => ("1px", "dotted"),
            BorderStyle.Double => ("3px", "double"),
            _ => ("1px", "solid"),
        };
        sb.Append($"border-{edge}:{width} {line} {HexColor(border.Color)};");
    }

    private static string HexColor(CellColor c) =>
        $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string EscapeHtml(string text) =>
        text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>
    /// Wraps an HTML fragment in the Windows CF_HTML clipboard descriptor: a header of
    /// byte-offset placeholders (StartHTML/EndHTML/StartFragment/EndFragment) followed by a
    /// minimal HTML document with <c>&lt;!--StartFragment--&gt;</c>/<c>&lt;!--EndFragment--&gt;</c>
    /// markers around the actual content, per the documented CF_HTML format. Offsets are counted
    /// in UTF-8 bytes (the format's requirement) using fixed-width 10-digit placeholders so the
    /// header's own length does not shift the offsets it describes.
    /// </summary>
    private static string WrapAsCfHtml(string fragment)
    {
        const string header =
            "Version:0.9\r\n" +
            "StartHTML:0000000000\r\n" +
            "EndHTML:0000000000\r\n" +
            "StartFragment:0000000000\r\n" +
            "EndFragment:0000000000\r\n";

        const string htmlStart = "<html><body>\r\n<!--StartFragment-->";
        const string htmlEnd = "<!--EndFragment-->\r\n</body></html>";

        var startHtml = Utf8Length(header);
        var startFragment = startHtml + Utf8Length(htmlStart);
        var endFragment = startFragment + Utf8Length(fragment);
        var endHtml = endFragment + Utf8Length(htmlEnd);

        var filledHeader =
            "Version:0.9\r\n" +
            $"StartHTML:{startHtml:D10}\r\n" +
            $"EndHTML:{endHtml:D10}\r\n" +
            $"StartFragment:{startFragment:D10}\r\n" +
            $"EndFragment:{endFragment:D10}\r\n";

        return filledHeader + htmlStart + fragment + htmlEnd;
    }

    private static int Utf8Length(string text) => Encoding.UTF8.GetByteCount(text);
}
