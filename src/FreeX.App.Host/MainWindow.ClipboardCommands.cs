using System;
using System.Collections.Generic;
using System.Linq;
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

        var text = ClipboardSerializer.Serialize(viewport, range);
        try { System.Windows.Clipboard.SetText(text); }
        catch { /* clipboard may be locked */ }

        // Show marching ants around the copied range
        SheetGrid.ClipboardRange = range;
        SheetGrid.ClipboardIsCut = isCut;

        // Capture raw cells (including formulas) for paste formula adjustment
        var sheet = _workbook.GetSheet(_currentSheetId);
        var clipCells = new List<(CellAddress, Cell)>();
        var pictureCells = CapturePictureCells(viewport, sheet, range);
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
            currentClipboardText = TryGetClipboardText();
            currentClipboardTextRead = true;
            if (!ClipboardPastePlanner.ShouldUseInternalClipboard(clip.Text, currentClipboardText))
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

                    var pasteCommand = PasteCommandFactory.CreateInternalPasteCommand(
                        _workbook,
                        _currentSheetId,
                        clip.SourceRange,
                        clip.Cells,
                        destinationRange,
                        ClipboardPastePlanner.ToCorePasteMode(mode),
                        options);
                    var command = keepColumnWidths
                        ? new CompositeWorkbookCommand(
                            "Paste Special",
                            [
                                pasteCommand,
                                new PasteColumnWidthsCommand(_currentSheetId, clip.SourceRange, currentRange.Start.Col)
                            ])
                        : pasteCommand;

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

    private static string? TryGetClipboardText()
    {
        try { return System.Windows.Clipboard.GetText(); }
        catch { return null; }
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

        // MoveRangeCommand only supports a same-sheet move.
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
        IWorkbookCommand CreatePastePictureCommand()
        {
            var currentRange = SheetGrid.SelectedRange ?? range;
            return new PasteRangeAsPictureCommand(
                _currentSheetId,
                clip.SourceRange,
                sourceCells,
                currentRange.Start,
                isLinkedPicture,
                sourceSheet?.Name);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreatePastePictureCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Paste Picture");
            return;
        }

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
}
