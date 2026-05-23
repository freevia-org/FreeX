using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private record InternalClipboard(GridRange SourceRange, List<(CellAddress Source, Cell Cell)> Cells, bool IsCut = false);
    private InternalClipboard? _internalClipboard;

    private void CancelCopyAndTransientModes()
    {
        ClearClipboardVisualState();
        _internalClipboard = null;
        CancelFormatPainter();
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

    private void PasteTransposeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ExecutePaste(PasteMode.All, new PasteSpecialOptions(Transpose: true));

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
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
            {
                var addr = new CellAddress(_currentSheetId, r, c);
                var cell = sheet?.GetCell(r, c);
                clipCells.Add((addr, cell?.Clone() ?? Cell.FromValue(BlankValue.Instance)));
            }
        }
        _internalClipboard = new InternalClipboard(range, clipCells, isCut);
    }

    private void ExecutePaste(PasteMode mode = PasteMode.All, PasteSpecialOptions options = default, bool keepColumnWidths = false)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        // If we have an internal clipboard (copied from within this app), use it with formula adjustment
        if (_internalClipboard is { } clip)
        {
            IWorkbookCommand CreatePasteCommand()
            {
                var currentRange = SheetGrid.SelectedRange ?? range;
                var pasteCommand = PasteCommandFactory.CreateInternalPasteCommand(
                    _workbook,
                    _currentSheetId,
                    clip.SourceRange,
                    clip.Cells,
                    currentRange.Start,
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

            _repeatPostAction = _ =>
            {
                CompletePasteSelection(clip.SourceRange, options);
                if (clip.IsCut)
                    _internalClipboard = null;
            };
            if (mode != PasteMode.Formats)
                RecalculateIfAutomatic(pasteOutcome.AffectedCells ?? []);

            CompletePasteSelection(clip.SourceRange, options);
            if (clip.IsCut)
                _internalClipboard = null;
            UpdateViewport();
            RefreshToolbar();
            return;
        }

        if (mode == PasteMode.Formats || mode == PasteMode.Formulas)
            return;

        if (mode == PasteMode.All && TryPasteClipboardImage(range.Start))
            return;

        // Fallback: external clipboard (plain text)
        string text;
        try { text = System.Windows.Clipboard.GetText(); }
        catch { return; }
        if (string.IsNullOrEmpty(text)) return;

        var rows = ClipboardSerializer.Deserialize(text);
        if (rows.Length == 0 || rows.All(r => r.Length == 0)) return;
        var capturedRows = rows.Select(row => (IReadOnlyList<string>)row).ToList();

        IWorkbookCommand CreateExternalPasteCommand()
        {
            var currentRange = SheetGrid.SelectedRange ?? range;
            return PasteCommandFactory.CreateExternalTextPasteCommand(
                _currentSheetId,
                currentRange.Start,
                capturedRows);
        }

        var fallbackOutcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateExternalPasteCommand);
        if (!fallbackOutcome.Success)
        {
            ShowCommandError(fallbackOutcome, "Paste");
            return;
        }

        _repeatPostAction = _ => CompleteExternalPasteSelection(capturedRows);
        RecalculateIfAutomatic(fallbackOutcome.AffectedCells ?? []);

        CompleteExternalPasteSelection(capturedRows);
        UpdateViewport();
        RefreshToolbar();
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

        _repeatPostAction = _ => CompletePasteSelection(clip.SourceRange, default);
        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        CompletePasteSelection(clip.SourceRange, default);
        UpdateViewport();
        RefreshToolbar();
    }

    private void CompletePasteSelection(GridRange sourceRange, PasteSpecialOptions options)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var pastedRows = options.Transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var pastedCols = options.Transpose ? sourceRange.RowCount : sourceRange.ColCount;
        var pastedEnd = new CellAddress(
            _currentSheetId,
            range.Start.Row + (uint)pastedRows - 1,
            range.Start.Col + (uint)pastedCols - 1);

        _selectionAnchor = range.Start;
        _selectionCursor = pastedEnd;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(range.Start, pastedEnd);
        ClearClipboardVisualState();
    }

    private void CompleteExternalPasteSelection(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (SheetGrid.SelectedRange is not { } range || rows.Count == 0)
            return;

        var pastedColCount = rows.Count == 0 ? 0 : rows.Max(row => row.Count);
        if (pastedColCount == 0)
            return;

        var pastedEnd = new CellAddress(
            _currentSheetId,
            range.Start.Row + (uint)rows.Count - 1,
            range.Start.Col + (uint)pastedColCount - 1);

        _selectionAnchor = range.Start;
        _selectionCursor = pastedEnd;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(range.Start, pastedEnd);
        ClearClipboardVisualState();
    }

    private bool TryPasteClipboardImage(CellAddress anchor)
    {
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
            var imageBytes = stream.ToArray();
            var pixelWidth = image.PixelWidth;
            var pixelHeight = image.PixelHeight;

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
        catch
        {
            return false;
        }
    }

    private void ExecuteClearSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Clear Contents",
                sheetId => new ClearContentsCommand(sheetId, GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId)),
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
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

        CompletePasteSelection(clip.SourceRange, new PasteSpecialOptions(Transpose: transpose));
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

        CompletePasteSelection(clip.SourceRange, new PasteSpecialOptions(Transpose: transpose));
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

        var sourceCells = clip.Cells
            .Select(c => (c.Item1, DrawingInputParser.FormatPictureCellText(c.Item2.Value)))
            .ToList();
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

        _repeatPostAction = _ => ClearClipboardVisualState();
        ClearClipboardVisualState();
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

        _repeatPostAction = _ => CompletePasteSelection(clip.SourceRange, new PasteSpecialOptions(Transpose: transpose));
        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        CompletePasteSelection(clip.SourceRange, new PasteSpecialOptions(Transpose: transpose));
        UpdateViewport();
        RefreshToolbar();
    }
}
