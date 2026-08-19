using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using Free.Shared.AppServices;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Editing;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    // clip-2 (R143): assigned in the MainWindow.xaml.cs constructor (defaults to a fresh instance
    // when no shared session is supplied, e.g. by every existing test's direct `new MainWindow(...)`)
    // rather than `= new()` here, so the DI-resolved production path can hand every window the same
    // process-wide WorkbookClipboardSession. See the constructor comment in MainWindow.xaml.cs.
    private readonly WorkbookClipboardSession _workbookClipboardSession;
    private readonly DrawingObjectClipboardSession _drawingObjectClipboard = new();

    private void CancelCopyAndTransientModes()
    {
        ClearClipboardMarqueeIfOwnedByThisWindow();
        _drawingObjectClipboard.Clear();
        CancelFormatPainter();
        _borderPickerSession.CancelDrawMode();
        SetSelectionMode(ExcelSelectionMode.Normal);
        SetEndMode(false);
    }

    private void ClearClipboardVisualState()
    {
        SheetGrid.ClipboardRange = null;
        SheetGrid.ClipboardRanges = null;
        SheetGrid.ClipboardIsCut = false;
    }

    /// <summary>
    /// R143-remediation (clip-2-regression): the shared fix for every purely LOCAL, no-clipboard-
    /// intent site (Escape, Delete/Clear Contents, Backspace, committing an ordinary cell edit, a
    /// structural edit) that needs to cancel a stale marching-ants marquee. Always clears THIS
    /// window's own marquee (<see cref="ClearClipboardVisualState"/> -- SheetGrid.ClipboardRange
    /// is per-window UI state, so that is always correct and idempotent), but only clears the
    /// SHARED <see cref="_workbookClipboardSession"/> when this window is the one that captured
    /// its current content (<see cref="WorkbookClipboardSession.ClearIfOwnedBy"/>). Without the
    /// ownership check, this same-process singleton (App.xaml.cs) meant one window's Escape/
    /// Delete/Backspace silently destroyed a DIFFERENT window's still-pasteable copy while that
    /// window kept showing marching ants around it, with no indication why the next Paste there
    /// produced nothing. A genuine new Copy (<see cref="ExecuteCopy"/>/<see
    /// cref="TryCopySelectedDrawingObject"/>) intentionally stays unconditional -- replacing the
    /// clipboard is exactly what a real Copy gesture should do, in any window, matching the OS
    /// clipboard.
    /// </summary>
    private void ClearClipboardMarqueeIfOwnedByThisWindow()
    {
        _workbookClipboardSession.ClearIfOwnedBy(this);
        ClearClipboardVisualState();
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
                new CommandOutcome(
                    false,
                    ClipboardFeedbackPlanner.MultiRangeSelectionUnsupported(isCut).Resolve(UiText.Get)),
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

        // R136-services-clipboard-formats-payload-parity (investigated, REFUTED): the internal
        // clipboard snapshot's own clipCells loop below skips AutoFilter-hidden rows via
        // IsRowFilterHidden, which suggested the plain-text/CF_HTML payload below (serialized off
        // fullRangeViewport) might still resurrect them. It does not: fullRangeViewport is built by
        // BuildFullRangeViewportForClipboard, which routes through ViewportService.GetViewport --
        // that materializer already skips any row where Sheet.IsRowEffectivelyHidden is true (folds
        // in FilterHiddenRows alongside HiddenRows/GroupHiddenRows), so a filter-hidden row's cells
        // were never present in this viewport to begin with, independent of clipCells' own guard.
        var text = ClipboardSerializer.Serialize(fullRangeViewport, copyRange);
        var clipboardMarker = WorkbookClipboardSession.CreateMarker();
        // Place plain text AND an HTML table fragment (CF_HTML) on the OS clipboard together,
        // matching real Excel: destination apps that understand HTML (Word, Outlook, browsers,
        // LibreOffice Calc) pick the richer format and preserve bold/fill/merges/number-format
        // display text, while anything HTML-unaware still gets the existing plain TSV text (M7).
        var customData = new List<PlatformClipboardData>();
        var html = BuildHtmlClipboardFragment(fullRangeViewport, sheet, copyRange, _workbook.Theme);
        if (!string.IsNullOrEmpty(html))
            customData.Add(PlatformClipboardData.FromText(System.Windows.DataFormats.Html, html));

        // R57-services-clipboard-formats-5-3: real Excel places a comma-delimited "CSV" clipboard
        // format alongside Text/Unicode Text/HTML on every cell-range copy, so a destination that
        // specifically enumerates for CSV (skipping plain Text) still gets a payload. Re-parse the
        // already-built TSV/newline `text` (same field values/escaping semantics as ClipboardSerializer
        // production, just re-delimited) and re-emit it RFC4180-quoted with commas.
        var csv = ClipboardCsvTextRenderer.Render(text);
        if (!string.IsNullOrEmpty(csv))
            customData.Add(PlatformClipboardData.FromText(
                System.Windows.DataFormats.CommaSeparatedValue,
                csv));

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
        PlatformClipboardImage? clipboardImage = null;
        var picturePlan = ClipboardRangePicturePlanner.TryBuild(ClipboardSerializer.Deserialize(text));
        if (TryRenderClipboardRangeBitmap(picturePlan) is { } clipboardBitmap)
        {
            clipboardImage = new PlatformClipboardImage(
                EncodeBitmapSourceToPng(clipboardBitmap),
                clipboardBitmap.PixelWidth,
                clipboardBitmap.PixelHeight);
        }

        SetClipboardDataWithRetry(WorkbookClipboardSession.AttachMarker(
            new PlatformClipboardContent(
                Text: text,
                Image: clipboardImage,
                CustomData: customData),
            clipboardMarker));

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
        // R143-remediation (clip-2-regression): pass `this` as the owner token so a later
        // no-clipboard-intent gesture in a DIFFERENT window (Escape/Delete/Backspace/an
        // unrelated edit) cannot clear what THIS window just copied -- see
        // ClearClipboardMarqueeIfOwnedByThisWindow and WorkbookClipboardSession.Owner.
        // external-refs-F1: capture the live source Sheet alongside the disconnected Cell clones
        // above, so a paste that lands in a DIFFERENT open window's workbook can still resolve
        // this range's hyperlinks/metadata (PasteCommandFactory can no longer look them up from
        // the destination workbook in that case -- see WorkbookClipboardSnapshot.SourceSheet).
        _workbookClipboardSession.Capture(new WorkbookClipboardSnapshot(
            copyRange,
            clipCells,
            pictureCells,
            text,
            isCut,
            areas.Count > 1 ? areas : null,
            clipboardMarker,
            SourceSheet: sheet),
            owner: this);
    }

    /// <summary>
    /// R91-io-clipboard-image-formats-5-1 (Chart/Shape), completed for Picture/TextBox by
    /// R92-consumer-wiring-sweep-2: captures a selected chart/shape/picture/text box into
    /// <see cref="_drawingObjectClipboard"/> instead of the cell-range clipboard, when
    /// SheetGrid currently has an object (not a plain cell) selected. Returns false (leaving both
    /// clipboards untouched) for any other selection kind, which keeps falling through to the
    /// pre-existing cell-range copy behavior unchanged.
    /// </summary>
    /// <remarks>
    /// R139-shared-clipboard-images (clipboard-drawing-object-no-os-clipboard-write): the
    /// in-process <see cref="_drawingObjectClipboard"/> capture above only ever served
    /// FreeX-to-FreeX paste (<see cref="PasteClipboardObject"/>) -- it never touched the real OS
    /// clipboard, so Ctrl+C on a chart/shape/picture/text box followed by Alt-Tab to Paint, Word, a
    /// browser, or even a SECOND FreeX window and Ctrl+V pasted nothing at all. Once an object is
    /// genuinely captured, also render it to a PNG-backed <see cref="PlatformClipboardImage"/> (best
    /// effort -- <see cref="TryRenderDrawingObjectClipboardImage"/> never throws) and place that on
    /// the OS clipboard, so external/cross-instance paste gets at least a picture, matching how the
    /// plain cell-range copy above always offers a Bitmap flavor. The internal
    /// <see cref="_drawingObjectClipboard"/> capture/paste path is completely unaffected either way.
    /// </remarks>
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
        var objectId = SheetGrid.SelectedObjectId;
        if (!_drawingObjectClipboard.TryCapture(
                _currentSheetId,
                kind,
                objectId,
                isCut))
            return false;

        // R143-remediation (clip-2-regression): unlike the no-clipboard-intent sites gated by
        // ClearClipboardMarqueeIfOwnedByThisWindow, this IS a genuine local Copy gesture (the
        // TryCapture above just succeeded), so it intentionally clears the shared cell-range
        // session UNCONDITIONALLY -- even if another window currently owns it -- exactly like
        // ExecuteCopy's own Capture() unconditionally overwrites whatever was there before. A new
        // Copy anywhere legitimately replaces "the clipboard", matching the real OS clipboard.
        _workbookClipboardSession.Clear();
        ClearClipboardVisualState();

        if (TryRenderDrawingObjectClipboardImage(kind!.Value, objectId) is { } clipboardImage)
            SetClipboardDataWithRetry(new PlatformClipboardContent(Image: clipboardImage));

        return true;
    }

    /// <summary>
    /// R139-shared-clipboard-images: renders the just-captured drawing object into a PNG-backed
    /// <see cref="PlatformClipboardImage"/> for the OS clipboard. Chart and Picture reuse the app's
    /// own existing chart-render (<see cref="FreeX.App.UI.ChartRenderer"/>) and image-decode
    /// (<see cref="FreeX.App.UI.WpfBitmapImageLoader"/>) pipelines for full fidelity, including
    /// whatever alpha channel the source picture bytes carry. Shape/TextBox have no isolated
    /// off-screen renderer of their own (GridView's shape painter is tied to a live on-screen paint
    /// pass), so they get a simple filled-rectangle-plus-text stand-in on an otherwise transparent
    /// background -- the same "smallest correct stand-in" precedent already used above for the plain
    /// cell-range picture flavor (<see cref="TryRenderClipboardRangeBitmap"/>). Returns null (never
    /// throws) when the object can no longer be found or nothing sensible can be rendered.
    /// </summary>
    private PlatformClipboardImage? TryRenderDrawingObjectClipboardImage(SelectionPaneObjectKind kind, Guid objectId)
    {
        try
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            if (sheet is null)
                return null;

            System.Windows.Media.Imaging.BitmapSource? bitmap = kind switch
            {
                SelectionPaneObjectKind.Chart => RenderChartClipboardBitmap(sheet, objectId),
                SelectionPaneObjectKind.Picture => RenderPictureClipboardBitmap(sheet, objectId),
                SelectionPaneObjectKind.Shape => RenderShapeClipboardBitmap(sheet, objectId),
                SelectionPaneObjectKind.TextBox => RenderTextBoxClipboardBitmap(sheet, objectId),
                _ => null,
            };
            if (bitmap is null)
                return null;

            return new PlatformClipboardImage(
                EncodeBitmapSourceToPng(bitmap),
                bitmap.PixelWidth,
                bitmap.PixelHeight);
        }
        catch
        {
            // Best-effort extra clipboard flavor -- never let a rendering hiccup fail the copy itself.
            return null;
        }
    }

    private System.Windows.Media.Imaging.BitmapSource? RenderChartClipboardBitmap(Sheet sheet, Guid objectId)
    {
        var chart = sheet.Charts.Find(c => c.Id == objectId);
        if (chart is null)
            return null;
        var viewport = SheetGrid.Viewport;
        if (viewport is null)
            return null;
        return FreeX.App.UI.ChartRenderer.Render(chart, viewport, _workbook.Theme)
            as System.Windows.Media.Imaging.BitmapSource;
    }

    private static System.Windows.Media.Imaging.BitmapSource? RenderPictureClipboardBitmap(Sheet sheet, Guid objectId)
    {
        var picture = sheet.Pictures.Find(p => p.Id == objectId);
        if (picture?.ImageBytes is not { Length: > 0 } imageBytes)
            return null;
        return FreeX.App.UI.WpfBitmapImageLoader.TryLoad(imageBytes, out var image)
            ? image as System.Windows.Media.Imaging.BitmapSource
            : null;
    }

    private System.Windows.Media.Imaging.BitmapSource? RenderShapeClipboardBitmap(Sheet sheet, Guid objectId)
    {
        var shape = sheet.DrawingShapes.Find(s => s.Id == objectId);
        if (shape is null)
            return null;

        var fill = shape.ResolveFillColor(_workbook.Theme, DrawingShapeModel.DefaultFillColor);
        var outline = shape.OutlineHasNoFill
            ? null
            : (CellColor?)shape.GetEffectiveOutlineColor(_workbook.Theme, DrawingShapeModel.DefaultOutlineColor);
        return RenderSimpleDrawingObjectBitmap(shape.Width, shape.Height, fill, outline, shape.ShapeText);
    }

    private System.Windows.Media.Imaging.BitmapSource? RenderTextBoxClipboardBitmap(Sheet sheet, Guid objectId)
    {
        var textBox = TextBoxModel.FindById(sheet.TextBoxes, objectId);
        if (textBox is null)
            return null;

        var fill = textBox.ResolveFillColor(_workbook.Theme, new CellColor(255, 255, 255));
        var outline = textBox.OutlineHasNoFill
            ? null
            : (CellColor?)textBox.GetEffectiveOutlineColor(_workbook.Theme, new CellColor(0, 0, 0));
        return RenderSimpleDrawingObjectBitmap(textBox.Width, textBox.Height, fill, outline, textBox.Text);
    }

    /// <summary>
    /// Smallest correct stand-in for a shape/text box's OS-clipboard picture flavor: a
    /// <see cref="System.Windows.Media.PixelFormats.Pbgra32"/> render target left fully transparent
    /// except where <paramref name="fill"/>/<paramref name="outline"/> actually paint, so a shape
    /// authored with no fill (or a text box's default "No Fill, No Line") pastes as a transparent
    /// picture rather than an opaque box -- matching how these objects render on screen.
    /// </summary>
    private static System.Windows.Media.Imaging.BitmapSource RenderSimpleDrawingObjectBitmap(
        double widthDip, double heightDip, CellColor? fill, CellColor? outline, string? text)
    {
        var width = Math.Max(1, (int)Math.Round(widthDip));
        var height = Math.Max(1, (int)Math.Round(heightDip));

        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, width, height);
            System.Windows.Media.SolidColorBrush? fillBrush = null;
            if (fill is { } f)
            {
                fillBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(f.R, f.G, f.B));
                fillBrush.Freeze();
            }

            System.Windows.Media.Pen? outlinePen = null;
            if (outline is { } o)
            {
                var outlineBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(o.R, o.G, o.B));
                outlineBrush.Freeze();
                outlinePen = new System.Windows.Media.Pen(outlineBrush, 1.5);
                outlinePen.Freeze();
            }

            if (fillBrush is not null || outlinePen is not null)
                dc.DrawRectangle(fillBrush, outlinePen, rect);

            if (!string.IsNullOrEmpty(text))
            {
                var typeface = new System.Windows.Media.Typeface("Segoe UI");
                var formatted = new System.Windows.Media.FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    12,
                    System.Windows.Media.Brushes.Black,
                    1.0)
                {
                    MaxTextWidth = Math.Max(1, width - 8),
                    MaxTextHeight = Math.Max(1, height - 8),
                    Trimming = TextTrimming.CharacterEllipsis,
                };
                dc.DrawText(formatted, new Point(4, 4));
            }
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
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

    /// <summary>
    /// R82-commands-cutcopy-clipboard-5-3: real Excel invalidates the OS clipboard once a
    /// Cut-then-Paste move completes -- the marching ants disappear and any further Ctrl+V is a
    /// no-op. Without this, the TSV/HTML payload <see cref="SetClipboardDataWithRetry"/> placed on
    /// the real OS clipboard during the original Ctrl+X stays there untouched even after
    /// the workbook clipboard session is cleared below, so <c>ExecutePaste</c>'s external-clipboard
    /// fallback (<see cref="TryGetClipboardText"/>/<see cref="TryGetClipboardHtml"/>) would happily
    /// paste that same cut content a second time. Best-effort: a transiently locked clipboard just
    /// leaves the stale cut text in place, matching how the other clipboard helpers in this file
    /// already treat OS-clipboard access as fallible.
    /// </summary>
    private void InvalidateOsClipboardAfterCutMove()
    {
        const int attempts = 20;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                var result = _platformClipboard.ClearAsync().AsTask().GetAwaiter().GetResult();
                if (result.IsSuccess)
                    return;
                throw new ExternalException(result.ErrorMessage);
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

    private void SetClipboardDataWithRetry(PlatformClipboardContent content)
    {
        _ = _platformClipboard.WriteAsync(content).AsTask().GetAwaiter().GetResult();
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

        // The request's generous per-row/per-column pixel bounds and its overflow clamp live in the
        // neutral tier (ClipboardViewportPlanner), shared with WorkbookSession's own clipboard copy
        // path, so this host cannot drift from it.
        return _viewportService.GetViewport(
            _workbook,
            _currentSheetId,
            ClipboardViewportPlanner.BuildFullRangeViewportRequest(range));
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
        if (!externalTextAsText && _workbookClipboardSession.HasContent)
        {
            var observation = ReadWorkbookClipboardForPastePlanning();
            currentClipboardText = observation.Text;
            currentClipboardTextRead = true;
            var resolution = _workbookClipboardSession.ResolvePaste(observation);
            if (resolution.Plan == ClipboardPastePlan.ReadFailed)
            {
                // A transient OS-clipboard read failure must not silently fall back to a stale
                // internal paste of the wrong content — skip the paste and tell the user.
                ShowCommandError(
                    new CommandOutcome(
                        false,
                        ClipboardFeedbackPlanner.ReadFailed.Resolve(UiText.Get)),
                    "Paste");
                return;
            }

            if (resolution.Plan == ClipboardPastePlan.UseExternalClipboardText)
            {
                ClearClipboardVisualState();
            }
            else if (resolution.Snapshot is { } clip)
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
                            clip.SourceAreas,
                            // external-refs-F1: clip.SourceSheet is the live Sheet captured at
                            // Copy time in the (possibly different) window that owned it -- pass
                            // it through so hyperlinks/rich-text/merged-regions/comments/CF still
                            // resolve for a cross-window paste, where _workbook.GetSheet(clip.
                            // SourceRange.Start.Sheet) below would otherwise always miss.
                            sourceSheetOverride: clip.SourceSheet);
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
                        _workbookClipboardSession.CompletePaste(clip);
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
                    _workbookClipboardSession.CompletePaste(clip);
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
            ? HtmlClipboardTableParser.Parse(htmlPayload)
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

    private string? TryGetClipboardText() => TryGetClipboardText(out _);

    private WorkbookClipboardReadObservation ReadWorkbookClipboardForPastePlanning()
    {
        const int attempts = 20;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var result = _platformClipboard.ReadAsync(WorkbookClipboardSession.PasteReadRequest)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            var observation = WorkbookClipboardSession.Observe(result);
            if (observation.Available && !observation.ReadFailed)
                return observation;
            if (result.Status is PlatformClipboardReadStatus.Unavailable
                or PlatformClipboardReadStatus.Unsupported)
                break;
            if (attempt < attempts)
                Thread.Sleep(50);
        }

        return new WorkbookClipboardReadObservation(
            Available: true,
            Text: null,
            Marker: null,
            ReadFailed: true);
    }

    /// <summary>
    /// Reads the OS clipboard text, distinguishing "read failed" (clipboard locked by another
    /// process) from "read succeeded but empty/non-text" — the paste planner must skip the paste
    /// on failure instead of falling back to a stale internal-clipboard paste (review P1).
    /// </summary>
    private string? TryGetClipboardText(out bool readFailed)
    {
        const int attempts = 20;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var result = _platformClipboard.ReadTextAsync().AsTask().GetAwaiter().GetResult();
            if (result.Status == PlatformClipboardReadStatus.Success)
            {
                readFailed = false;
                return result.Value;
            }
            if (result.Status == PlatformClipboardReadStatus.Empty)
            {
                readFailed = false;
                return null;
            }
            if (result.Status is PlatformClipboardReadStatus.Unavailable
                or PlatformClipboardReadStatus.Unsupported)
                break;
            if (attempt < attempts)
                Thread.Sleep(50);
        }

        readFailed = true;
        return null;
    }

    private bool TryClipboardContainsImage()
    {
        var result = _platformClipboard.ReadImageAsync().AsTask().GetAwaiter().GetResult();
        return result.Status == PlatformClipboardReadStatus.Success && result.Value is not null;
    }

    /// <summary>
    /// R91-io-clipboard-image-formats-5-4: looks for a raw, alpha-preserving "PNG" (or "image/png")
    /// clipboard entry alongside the flattened CF_DIB/CF_BITMAP one that <see cref="System.Windows.Clipboard.GetImage"/>
    /// always resolves to. Chrome/Edge and many image editors place both when copying a
    /// transparent-background image; returns the raw PNG bytes (with alpha intact) when found, or
    /// null when no such format is present/readable so the caller falls back to the flattened image.
    /// </summary>
    private byte[]? TryGetClipboardPngFormatBytes()
    {
        var request = new PlatformClipboardReadRequest(
            CustomFormats: PngClipboardFormatNames
                .Select(static name => new PlatformClipboardFormat(
                    name,
                    PlatformClipboardDataKind.Bytes))
                .ToArray());
        var result = _platformClipboard.ReadAsync(request).AsTask().GetAwaiter().GetResult();
        if (result.Status != PlatformClipboardReadStatus.Success || result.Value is null)
            return null;
        foreach (var formatName in PngClipboardFormatNames)
        {
            if (result.Value.GetBytes(formatName) is { Length: > 0 } bytes)
                return bytes;
        }
        return null;
    }

    private static readonly string[] PngClipboardFormatNames = ["PNG", "image/png"];

    /// <summary>
    /// R132-io-clipboard-png-decode-fallback: resolves the bytes/dimensions <see
    /// cref="TryPasteClipboardImage"/> should paste, preferring an alpha-preserving raw "PNG"
    /// clipboard format (see <see cref="TryGetClipboardPngFormatBytes"/>) when the source app
    /// placed one and its bytes actually decode. A sibling "PNG" entry can be PRESENT but not
    /// itself decodable (a broken/unsupported PNG flavor some source apps advertise) -- when that
    /// decode throws, this falls back to the flattened DIB/CF_BITMAP entry exactly like the
    /// "no PNG entry at all" case, instead of failing the whole paste when a perfectly good bitmap
    /// flavor is sitting right there. Split out (rather than inlined in TryPasteClipboardImage) so
    /// the decode-then-fallback decision has direct unit coverage without needing the real OS
    /// clipboard/STA thread that the R49/R57/R82/R91 integration clipboard tests already rely on.
    /// </summary>
    /// <param name="pngFormatBytes">
    /// The raw bytes from a sibling "PNG"/"image/png" clipboard entry, or null when none was
    /// present (<see cref="TryGetClipboardPngFormatBytes"/>'s result).
    /// </param>
    /// <param name="containsFlattenedImage">Probes for a flattened DIB/CF_BITMAP clipboard entry.</param>
    /// <param name="getFlattenedImage">Reads the flattened DIB/CF_BITMAP entry as a BitmapSource.</param>
    internal static bool TryResolveClipboardImageBytes(
        byte[]? pngFormatBytes,
        Func<bool> containsFlattenedImage,
        Func<System.Windows.Media.Imaging.BitmapSource?> getFlattenedImage,
        out byte[]? imageBytes,
        out int pixelWidth,
        out int pixelHeight)
    {
        if (pngFormatBytes is not null &&
            TryDecodePngFormatBytes(pngFormatBytes, out pixelWidth, out pixelHeight))
        {
            imageBytes = pngFormatBytes;
            return true;
        }

        if (!containsFlattenedImage())
        {
            imageBytes = null;
            pixelWidth = 0;
            pixelHeight = 0;
            return false;
        }

        var image = getFlattenedImage();
        if (image is null)
        {
            imageBytes = null;
            pixelWidth = 0;
            pixelHeight = 0;
            return false;
        }

        imageBytes = EncodeBitmapSourceToPng(image);
        pixelWidth = image.PixelWidth;
        pixelHeight = image.PixelHeight;
        return true;
    }

    private static byte[] EncodeBitmapSourceToPng(
        System.Windows.Media.Imaging.BitmapSource image)
    {
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var stream = new System.IO.MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Attempts to decode raw bytes as a PNG image, returning false (instead of throwing) when the
    /// bytes are not actually valid/decodable PNG data -- see <see cref="TryResolveClipboardImageBytes"/>.
    /// </summary>
    private static bool TryDecodePngFormatBytes(byte[] pngFormatBytes, out int pixelWidth, out int pixelHeight)
    {
        try
        {
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                new System.IO.MemoryStream(pngFormatBytes),
                System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            pixelWidth = frame.PixelWidth;
            pixelHeight = frame.PixelHeight;
            return true;
        }
        catch
        {
            pixelWidth = 0;
            pixelHeight = 0;
            return false;
        }
    }

    /// <summary>Reads the CF_HTML clipboard payload (header + fragment), or null when absent/unreadable.</summary>
    private string? TryGetClipboardHtml()
    {
        var result = _platformClipboard.ReadCustomAsync(new PlatformClipboardFormat(
                System.Windows.DataFormats.Html,
                PlatformClipboardDataKind.Text))
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return result.Status == PlatformClipboardReadStatus.Success
            ? result.Value?.Text
            : null;
    }

    private void ExecuteInsertCopiedCells()
    {
        if (_workbookClipboardSession.Content is not { } clip || SheetGrid.SelectedRange is not { } range)
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
            _workbookClipboardSession.CompletePaste(clip);
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
        WorkbookClipboardSnapshot clip,
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
            var pngFormatBytes = TryGetClipboardPngFormatBytes();
            if (pngFormatBytes is not null
                && TryDecodePngFormatBytes(pngFormatBytes, out pixelWidth, out pixelHeight))
            {
                imageBytes = pngFormatBytes;
            }
            else
            {
                var imageRead = _platformClipboard.ReadImageAsync().AsTask().GetAwaiter().GetResult();
                if (imageRead.Status != PlatformClipboardReadStatus.Success
                    || imageRead.Value is not { PngBytes.Length: > 0 } image)
                    return false;
                imageBytes = image.PngBytes;
                pixelWidth = image.PixelWidth ?? 0;
                pixelHeight = image.PixelHeight ?? 0;
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
        // R143-remediation (clip-2-regression): Delete carries no clipboard intent, so this must
        // only cancel a marquee/session THIS window owns -- see
        // ClearClipboardMarqueeIfOwnedByThisWindow.
        ClearClipboardMarqueeIfOwnedByThisWindow();

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

        // R143-remediation (clip-2-regression): Backspace carries no clipboard intent, so this
        // must only cancel a marquee/session THIS window owns -- see
        // ClearClipboardMarqueeIfOwnedByThisWindow.
        ClearClipboardMarqueeIfOwnedByThisWindow();

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
        if (!_workbookClipboardSession.HasContent)
        {
            string text;
            text = TryGetClipboardText() ?? string.Empty;
            if (string.IsNullOrEmpty(text)) return;
        }

        var dlg = new PasteSpecialDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var plan = PasteSpecialPlanner.CreatePlan(dlg.Selection);
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
        if (_workbookClipboardSession.Content is not { } clip || SheetGrid.SelectedRange is not { } range)
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
            _workbookClipboardSession.CompletePaste(clip);
        UpdateViewport();
        RefreshToolbar();
    }

    private void ExecutePasteComments(bool transpose)
    {
        if (_workbookClipboardSession.Content is not { } clip || SheetGrid.SelectedRange is not { } range)
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
            _workbookClipboardSession.CompletePaste(clip);
        UpdateViewport();
        RefreshToolbar();
    }

    private void ExecutePasteValidation(bool transpose)
    {
        if (_workbookClipboardSession.Content is not { } clip || SheetGrid.SelectedRange is not { } range)
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
            _workbookClipboardSession.CompletePaste(clip);
        UpdateViewport();
        RefreshToolbar();
    }

    private void ExecutePasteAsPicture(bool isLinkedPicture)
    {
        if (_workbookClipboardSession.Content is not { } clip || SheetGrid.SelectedRange is not { } range)
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
            _workbookClipboardSession.CompletePaste(clip);
        UpdateViewport();
        RefreshToolbar();
    }

    private void ExecutePasteLink(bool transpose, bool keepColumnWidths = false)
    {
        if (_workbookClipboardSession.Content is not { } clip || SheetGrid.SelectedRange is not { } range)
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
            _workbookClipboardSession.CompletePaste(clip);
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
    /// SparklineValueCache/WorkbookSelectionStatsCache (both keyed on that revision) keep returning their
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
    // HtmlClipboardTableParser) recovers the pasted table's actual <tr>/<td> row/column
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
    private static System.Windows.Media.Imaging.BitmapSource? TryRenderClipboardRangeBitmap(
        ClipboardRangePicturePlan? plan)
    {
        try
        {
            if (plan is null)
                return null;

            var width = plan.PixelWidth;
            var height = plan.PixelHeight;
            var backgroundBrush = CreateBrush(ClipboardRangePicturePlanner.BackgroundColor);
            var gridBrush = CreateBrush(ClipboardRangePicturePlanner.GridlineColor);
            var textBrush = CreateBrush(ClipboardRangePicturePlanner.TextColor);

            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(backgroundBrush, null, new Rect(0, 0, width, height));

                var gridPen = new System.Windows.Media.Pen(gridBrush, 1);
                gridPen.Freeze();
                var typeface = new System.Windows.Media.Typeface("Segoe UI");
                for (var r = 0; r < plan.RowCount; r++)
                {
                    for (var c = 0; c < plan.ColumnCount; c++)
                    {
                        var cellRect = new Rect(
                            c * ClipboardRangePicturePlanner.CellWidth,
                            r * ClipboardRangePicturePlanner.CellHeight,
                            ClipboardRangePicturePlanner.CellWidth,
                            ClipboardRangePicturePlanner.CellHeight);
                        dc.DrawRectangle(null, gridPen, cellRect);

                        var cellText = plan.TextAt(r, c);
                        if (string.IsNullOrEmpty(cellText))
                            continue;

                        var formatted = new System.Windows.Media.FormattedText(
                            cellText,
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            ClipboardRangePicturePlanner.FontSize,
                            textBrush,
                            1.0)
                        {
                            MaxTextWidth = Math.Max(
                                1,
                                ClipboardRangePicturePlanner.CellWidth
                                - (2 * ClipboardRangePicturePlanner.TextPaddingHorizontal)),
                            MaxTextHeight = Math.Max(
                                1,
                                ClipboardRangePicturePlanner.CellHeight
                                - (2 * ClipboardRangePicturePlanner.TextPaddingVertical)),
                            Trimming = TextTrimming.CharacterEllipsis
                        };
                        dc.DrawText(
                            formatted,
                            new Point(
                                cellRect.Left + ClipboardRangePicturePlanner.TextPaddingHorizontal,
                                cellRect.Top + ClipboardRangePicturePlanner.TextPaddingVertical));
                    }
                }
            }

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                Math.Max(1, width),
                Math.Max(1, height),
                96,
                96,
                System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;

            static System.Windows.Media.SolidColorBrush CreateBrush(ClipboardRangePictureColor color)
            {
                var brush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(color.Red, color.Green, color.Blue));
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
            // Best-effort extra clipboard flavor -- never let a rendering hiccup fail the copy itself.
            return null;
        }
    }
}
