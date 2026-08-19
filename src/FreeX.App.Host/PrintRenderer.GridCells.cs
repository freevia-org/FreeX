using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    // The WPF print path renders cell fills, per-cell borders, font colors, rotated text, and
    // overflow-into-blank-neighbor text the same way the interactive grid (GridView.Rendering.cs)
    // does, reading DisplayCell.Style — which the viewport already merges conditional formatting
    // into — so CF highlight/colorscale fills and dxf font colors print exactly as displayed.
    // Data Bars and Icon Sets are separate DisplayCell fields (ConditionalDataBar/ConditionalIcon,
    // not part of the merged Style), so they're drawn via the interactive grid's own
    // GridView.DrawConditionalDataBar/DrawConditionalIcon helpers (made public for exactly this
    // cross-assembly reuse) rather than being reimplemented here.
    // Page Setup > Sheet > "Black and white" (Sheet.PrintBlackAndWhite) is threaded in here the
    // same way RenderPageVisual (PrintRenderer.HeaderFooter.cs) already threads it to
    // DrawDisplayedComments: fills are suppressed (no color/pattern paint), and borders/gridlines
    // are forced to solid black instead of their authored/light-gray color, matching Excel's
    // grayscale print preview.
    private static readonly Pen BlackAndWhiteGridlinePen = new(Brushes.Black, 0.5);

    // Matches GridView.cs's ValidationCirclePen (red 226,28,33 @ 1.5pt) exactly, so the printed
    // circle is visually identical to the on-screen Circle Invalid Data overlay.
    private static readonly Pen PrintedValidationCirclePen =
        new(
            new SolidColorBrush(Color.FromRgb(
                ValidationCircleLayoutPlanner.StrokeColor.R,
                ValidationCircleLayoutPlanner.StrokeColor.G,
                ValidationCircleLayoutPlanner.StrokeColor.B)),
            ValidationCircleLayoutPlanner.StrokeThickness);

    private static void DrawPrintedGridCells(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        ICollection<PdfLinkOverlay> linkOverlays,
        ICollection<PdfCellDestinationOverlay> cellDestinationOverlays,
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyDictionary<(uint Row, uint Col), WorksheetPrintHyperlinkPlan> hyperlinkLookup,
        IReadOnlyDictionary<(uint Row, uint Col), CellAddress> cellDestinationLookup,
        bool printGridlines,
        WorksheetPrintErrorValue printErrorValue,
        double gridLeft,
        double gridTop,
        Workbook workbook,
        bool blackAndWhite = false,
        Sheet? sheet = null)
    {
        // Multi-pass rendering (fills, then gridlines/borders, then text) mirrors the interactive
        // grid's layering (GridView.Rendering.cs). A single combined per-cell pass would have the
        // NEXT column's fill (drawn on the following loop iteration) paint over text that the
        // PREVIOUS cell already overflowed into that column, clipping it. Drawing every fill first,
        // then every border, then every piece of text — including overflow text — guarantees text
        // always layers on top of all fills/borders regardless of which cell "owns" the space it
        // spills into.
        for (var rowIndex = 0; rowIndex < pageRows.Count; rowIndex++)
        {
            var row = pageRows[rowIndex];
            var rowHeight = measurement.RowHeightAt(rowIndex);
            var y = gridTop + measurement.RowOffset(rowIndex);
            for (var colIndex = 0; colIndex < pageColumns.Count; colIndex++)
            {
                var col = pageColumns[colIndex];
                var colWidth = measurement.ColumnWidthAt(colIndex);
                var x = gridLeft + measurement.ColumnOffset(colIndex);
                var cellRect = new Rect(x, y, colWidth, rowHeight);

                cellLookup.TryGetValue((row, col), out var cell);
                var style = cell.Style;

                // Excel's "Black and white" print option forces every fill to transparent/white --
                // never merely dimmed or grayscaled -- so skip the fill entirely instead of drawing it.
                if (!blackAndWhite && style is not null && WorksheetPrintCellGeometryPlanner.HasVisibleFill(style))
                {
                    DrawPrintedCellFill(dc, cellRect, style);
                }
            }
        }

        for (var rowIndex = 0; rowIndex < pageRows.Count; rowIndex++)
        {
            var row = pageRows[rowIndex];
            var rowHeight = measurement.RowHeightAt(rowIndex);
            var y = gridTop + measurement.RowOffset(rowIndex);
            for (var colIndex = 0; colIndex < pageColumns.Count; colIndex++)
            {
                var col = pageColumns[colIndex];
                var colWidth = measurement.ColumnWidthAt(colIndex);
                var x = gridLeft + measurement.ColumnOffset(colIndex);
                var cellRect = new Rect(x, y, colWidth, rowHeight);

                cellLookup.TryGetValue((row, col), out var cell);
                var style = cell.Style;

                // Merge membership: suppress the gridline/border edges that fall strictly inside a
                // merged region (i.e. another cell of the same merge sits on that side) so only the
                // merge's outer perimeter ever prints -- matching Excel (and the on-screen
                // GridView.Rendering.cs Pass 2), which never shows an interior line through a merged
                // range, whether that line comes from the default gridline grid or an explicit
                // user-authored border.
                var merge = sheet?.GetMergeRegion(new CellAddress(sheet.Id, row, col));
                var suppressTop = merge is { } mTop && row > mTop.Start.Row;
                var suppressBottom = merge is { } mBottom && row < mBottom.End.Row;
                var suppressLeft = merge is { } mLeft && col > mLeft.Start.Col;
                var suppressRight = merge is { } mRight && col < mRight.End.Col;

                if (printGridlines)
                {
                    var gridlinePen = blackAndWhite ? BlackAndWhiteGridlinePen : new Pen(Brushes.LightGray, 0.5);
                    DrawPrintedGridlineEdges(dc, cellRect, gridlinePen, suppressTop, suppressBottom, suppressLeft, suppressRight);
                }

                if (style is not null && HasVisiblePrintedBorder(style))
                {
                    // Shared-edge precedence: resolve each edge against the ACTUAL neighboring
                    // cell's opposing border via the same heaviest-wins rule the interactive grid
                    // uses (GridView.Rendering.cs's borderStyleLookup + ResolveBorderEdgeWinner),
                    // instead of always painting this cell's own border last-drawn-wins-over --
                    // otherwise draw order (later column/row overwrites the earlier one) would
                    // silently downgrade e.g. a Double edge to a neighbor's plain Thin.
                    var topWinner = WorksheetPrintCellGeometryPlanner.ResolveBorderWinner(
                        style.BorderTop,
                        WorksheetPrintCellGeometryPlanner.ResolveNeighborBorder(cellLookup, row - 1, col, s => s.BorderBottom));
                    var bottomWinner = WorksheetPrintCellGeometryPlanner.ResolveBorderWinner(
                        style.BorderBottom,
                        WorksheetPrintCellGeometryPlanner.ResolveNeighborBorder(cellLookup, row + 1, col, s => s.BorderTop));
                    var leftWinner = WorksheetPrintCellGeometryPlanner.ResolveBorderWinner(
                        style.BorderLeft,
                        WorksheetPrintCellGeometryPlanner.ResolveNeighborBorder(cellLookup, row, col - 1, s => s.BorderRight));
                    var rightWinner = WorksheetPrintCellGeometryPlanner.ResolveBorderWinner(
                        style.BorderRight,
                        WorksheetPrintCellGeometryPlanner.ResolveNeighborBorder(cellLookup, row, col + 1, s => s.BorderLeft));

                    // Diagonal-on-merge: a diagonal border must span the full merged rectangle,
                    // not just the anchor's own un-merged footprint (mirrors GridView.Rendering.cs's
                    // diagonalW/diagonalH widening). Only the merge's anchor cell ever draws it.
                    var isMergeAnchor = merge is not { } anchorMerge || (row == anchorMerge.Start.Row && col == anchorMerge.Start.Col);
                    var diagonalWidth = colWidth;
                    var diagonalHeight = rowHeight;
                    if (isMergeAnchor && merge is { } diagonalMerge &&
                        (style.BorderDiagonalDown.Style != BorderStyle.None || style.BorderDiagonalUp.Style != BorderStyle.None))
                    {
                        diagonalWidth = WorksheetPrintCellGeometryPlanner.MeasureMergedColumnSpan(
                            measurement, pageColumns, colIndex, diagonalMerge.End.Col);
                        diagonalHeight = WorksheetPrintCellGeometryPlanner.MeasureMergedRowSpan(
                            measurement, pageRows, rowIndex, diagonalMerge.End.Row);
                    }

                    DrawPrintedCellBorders(
                        dc,
                        cellRect,
                        style,
                        blackAndWhite,
                        suppressTop,
                        suppressBottom,
                        suppressLeft,
                        suppressRight,
                        topWinner,
                        bottomWinner,
                        leftWinner,
                        rightWinner,
                        isMergeAnchor,
                        diagonalWidth,
                        diagonalHeight);
                }
            }
        }

        for (var rowIndex = 0; rowIndex < pageRows.Count; rowIndex++)
        {
            var row = pageRows[rowIndex];
            var rowHeight = measurement.RowHeightAt(rowIndex);
            var y = gridTop + measurement.RowOffset(rowIndex);
            for (var colIndex = 0; colIndex < pageColumns.Count; colIndex++)
            {
                var col = pageColumns[colIndex];
                var colWidth = measurement.ColumnWidthAt(colIndex);
                var x = gridLeft + measurement.ColumnOffset(colIndex);

                // Merge-span widening: mirrors GridView.Rendering.cs's Pass 3 (lines 835-841), which
                // sums every merged column's width / merged row's height into the rect BEFORE laying
                // out text (and before drawing data bars/icon sets into that same rect), so a merged
                // banner cell's text is measured/clipped against its true multi-column footprint
                // instead of just its single anchor cell (R90-render-cell-overflow-clip-5-1). Only the
                // merge's anchor cell widens; a non-anchor member cell has no display text of its own
                // (Excel clears it on merge) so it draws nothing here regardless.
                var textMerge = sheet?.GetMergeRegion(new CellAddress(sheet.Id, row, col));
                var cellWidth = colWidth;
                var cellHeight = rowHeight;
                if (textMerge is { } tm && row == tm.Start.Row && col == tm.Start.Col)
                {
                    cellWidth = WorksheetPrintCellGeometryPlanner.MeasureMergedColumnSpan(
                        measurement, pageColumns, colIndex, tm.End.Col);
                    cellHeight = WorksheetPrintCellGeometryPlanner.MeasureMergedRowSpan(
                        measurement, pageRows, rowIndex, tm.End.Row);
                }
                var cellRect = new Rect(x, y, cellWidth, cellHeight);

                cellLookup.TryGetValue((row, col), out var cell);
                var style = cell.Style;

                if (hyperlinkLookup.TryGetValue((row, col), out var link))
                {
                    linkOverlays.Add(new PdfLinkOverlay(
                        link.Target,
                        link.TargetKind,
                        x,
                        y,
                        colWidth,
                        rowHeight,
                        link.SourceAddress,
                        link.TargetAddress));
                }

                if (cellDestinationLookup.TryGetValue((row, col), out var destinationAddress))
                {
                    cellDestinationOverlays.Add(new PdfCellDestinationOverlay(
                        destinationAddress,
                        x,
                        y,
                        colWidth,
                        rowHeight));
                }

                // Mirror GridView.Rendering.cs's own CF pass: data bars and icon sets are separate
                // DisplayCell fields (ConditionalDataBar/ConditionalIcon) from the style-merged fill
                // this method already prints via the shared fill policy and DrawPrintedCellFill above, so
                // they need their own draw calls here or Print/Print Preview silently omits them.
                var textRect = cellRect;
                if (cell.ConditionalDataBar is { } dataBar)
                {
                    GridView.DrawConditionalDataBar(dc, dataBar, cellRect);
                }

                if (cell.ConditionalIcon is { } icon)
                {
                    // Mirrors the interactive grid's own RTL resolution (GridView.Rendering.cs's
                    // CalculateConditionalIconCellLayout(rect, icon, IsSheetRightToLeft) call) so a
                    // right-to-left sheet's icon set prints on the same edge it renders on screen,
                    // instead of always mirroring as if the sheet were left-to-right.
                    var iconIsRightToLeft = CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft(
                        style?.ReadingOrder ?? CellReadingOrder.Context, sheet?.IsRightToLeft ?? false);
                    var iconLayout = GridView.CalculateConditionalIconCellLayout(cellRect, icon, iconIsRightToLeft);
                    GridView.DrawConditionalIcon(dc, icon, iconLayout.IconRect);
                    if (!iconLayout.ShouldDrawText || string.IsNullOrEmpty(cell.DisplayText))
                        continue;
                    textRect = iconLayout.TextRect;
                }

                if (string.IsNullOrEmpty(cell.DisplayText))
                {
                    continue;
                }

                var displayText = FormatPrintedCellText(cell.DisplayText, printErrorValue);
                if (string.IsNullOrEmpty(displayText))
                    continue;

                DrawPrintedCellText(
                    dc,
                    textOverlays,
                    cell,
                    style,
                    displayText,
                    textRect,
                    measurement,
                    pageColumns,
                    colIndex,
                    row,
                    cellLookup,
                    textMerge,
                    sheet,
                    blackAndWhite);
            }
        }

        // Sparklines are a screen-only overlay drawn above the grid (GridView.Overlays.Sparklines.cs
        // RenderSparklines), not part of DisplayCell.Style or any of the per-cell fields already
        // printed above -- like the ConditionalDataBar/ConditionalIcon pass, they need their own
        // draw call here or Print/Print Preview/PDF/XPS silently omits them entirely
        // (R88-render-sparkline-5-1).
        if (sheet is { Sparklines.Count: > 0 })
        {
            DrawPrintedSparklines(dc, workbook, sheet, measurement, pageRows, pageColumns, gridLeft, gridTop);
        }

        // Data > Data Validation > Circle Invalid Data is a screen-only overlay drawn above the
        // grid (GridView.Overlays.cs RenderValidationCircles) that reads a shell-side
        // DependencyProperty the interactive grid instance owns -- Sheet.ValidationCircleCells is
        // the sheet/session-level mirror of that same circled-cell set specifically so a headless
        // consumer like this print path can read it too. Without this call Print/Print
        // Preview/PDF/XPS silently omitted every circle while still printing the cell's plain
        // value/fill/borders (R91-print-twin-two-tier-sweep-1), just like the sparkline gap above.
        if (sheet is { ValidationCircleCells.Count: > 0 })
        {
            DrawPrintedValidationCircles(dc, sheet.ValidationCircleCells, measurement, pageRows, pageColumns, gridLeft, gridTop);
        }
    }

    /// <summary>
    /// Mirrors GridView.Overlays.cs's RenderValidationCircles geometry (same 0.38/0.32
    /// width/height-fraction radii, same red ellipse stroke) but against the printed page's own
    /// row/column measurement instead of the interactive viewport's on-screen row/column offsets,
    /// so a printed/PDF page shows the identical red validation-circle ovals the user sees live.
    /// </summary>
    private static void DrawPrintedValidationCircles(
        DrawingContext dc,
        IReadOnlyList<CellAddress> circledCells,
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        double gridLeft,
        double gridTop)
    {
        var rowIndexLookup = new Dictionary<uint, int>(pageRows.Count);
        for (var i = 0; i < pageRows.Count; i++)
            rowIndexLookup[pageRows[i]] = i;

        var colIndexLookup = new Dictionary<uint, int>(pageColumns.Count);
        for (var i = 0; i < pageColumns.Count; i++)
            colIndexLookup[pageColumns[i]] = i;

        foreach (var cell in circledCells)
        {
            if (!rowIndexLookup.TryGetValue(cell.Row, out var rowIndex) ||
                !colIndexLookup.TryGetValue(cell.Col, out var colIndex))
            {
                continue;
            }

            var colWidth = measurement.ColumnWidthAt(colIndex);
            var rowHeight = measurement.RowHeightAt(rowIndex);
            if (colWidth <= 0 || rowHeight <= 0)
                continue;

            var rect = new Rect(
                gridLeft + measurement.ColumnOffset(colIndex),
                gridTop + measurement.RowOffset(rowIndex),
                colWidth,
                rowHeight);

            var ellipse = ValidationCircleLayoutPlanner.CalculateEllipseBounds(
                new LayoutRect(rect.Left, rect.Top, rect.Width, rect.Height));
            var center = new Point(
                ellipse.Left + (ellipse.Width / 2.0),
                ellipse.Top + (ellipse.Height / 2.0));
            dc.DrawEllipse(
                null,
                PrintedValidationCirclePen,
                center,
                ellipse.Width / 2.0,
                ellipse.Height / 2.0);
        }
    }

    private static void DrawPrintedSparklines(
        DrawingContext dc,
        Workbook workbook,
        Sheet sheet,
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        double gridLeft,
        double gridTop)
    {
        var rowIndexLookup = new Dictionary<uint, int>(pageRows.Count);
        for (var i = 0; i < pageRows.Count; i++)
            rowIndexLookup[pageRows[i]] = i;

        var colIndexLookup = new Dictionary<uint, int>(pageColumns.Count);
        for (var i = 0; i < pageColumns.Count; i++)
            colIndexLookup[pageColumns[i]] = i;

        var sparklineValues = FreeX.App.Presentation.Sparklines.SparklineSeriesReader.BuildValues(workbook, sheet);

        var axisScalePlan = FreeX.App.Presentation.Sparklines.SparklineAxisScalePlanner.Build(
            sheet.Sparklines,
            sparklineValues);

        foreach (var sparkline in sheet.Sparklines)
        {
            if (!rowIndexLookup.TryGetValue(sparkline.Location.Row, out var rowIndex) ||
                !colIndexLookup.TryGetValue(sparkline.Location.Col, out var colIndex) ||
                !sparklineValues.TryGetValue(sparkline.Id, out var values) ||
                values.Count == 0)
            {
                continue;
            }

            // Match the interactive grid's own 3px inset (GridView.Overlays.Sparklines.cs
            // RenderSparklines) so the printed sparkline sits inside the cell border exactly like
            // the on-screen one.
            var colWidth = measurement.ColumnWidthAt(colIndex);
            var rowHeight = measurement.RowHeightAt(rowIndex);
            var rect = new Rect(
                gridLeft + measurement.ColumnOffset(colIndex) + 3,
                gridTop + measurement.RowOffset(rowIndex) + 3,
                Math.Max(1, colWidth - 6),
                Math.Max(1, rowHeight - 6));

            GridView.DrawSparklineIntoCell(dc, sparkline, values, rect, axisScalePlan);
        }
    }

    private static void DrawPrintedCellText(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        DisplayCell cell,
        CellStyle? style,
        string displayText,
        Rect rect,
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageColumns,
        int colIndex,
        uint row,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        GridRange? merge,
        Sheet? sheet,
        bool blackAndWhite = false)
    {
        var textRotation = style?.TextRotation ?? 0;
        var renderText = CellTextOrientationLayoutPlanner.PrepareDisplayText(displayText, textRotation);
        var hasOrientation = CellTextOrientationLayoutPlanner.HasTextOrientation(textRotation);
        // Excel's "Black and white" print option forces every font color to black regardless of
        // its authored color, matching the same flag already applied to comments/gridlines/borders.
        Brush textBrush;
        Color textColor;
        if (blackAndWhite)
        {
            textBrush = Brushes.Black;
            textColor = Colors.Black;
        }
        else
        {
            textBrush = ResolvePrintedTextBrush(style, out textColor);
        }
        var wrapText = style?.WrapText == true;

        // Mirrors the interactive grid's own reading-order resolution (GridView.Rendering.cs's
        // isEffectivelyRightToLeft, fed by CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft)
        // instead of hardcoding LTR: a General-aligned numeric/text cell on a right-to-left sheet
        // must anchor to the opposite edge on the printed page/PDF, exactly as it does on screen.
        var isEffectivelyRightToLeft = CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft(
            style?.ReadingOrder ?? CellReadingOrder.Context, sheet?.IsRightToLeft ?? false);
        var flowDirection = isEffectivelyRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        // Read the cell's own font (size/name/bold/italic) instead of a fixed print font, so a
        // 24pt Bold title prints exactly as it displays on screen (GridView.Rendering.cs:305).
        var typeface = ResolvePrintedCellTypeface(style);
        var fontSize = ResolvePrintedCellFontSizeDip(style);

        var maxTextWidth = Math.Max(1, rect.Width - 4);

        // Shrink-to-fit is honored on screen (GridView.Rendering.cs:309) — mirror the same
        // width-driven font shrink here so a shrink-to-fit cell prints shrunk instead of
        // overflowing/ellipsis-truncated at the fixed print font size.
        if (style?.ShrinkToFit == true && !wrapText)
        {
            fontSize = GridView.ResolveShrinkFontSize(
                fontSize,
                maxTextWidth,
                size => MeasurePrintedSingleLineText(renderText, typeface, size).Width,
                PointsToPrintedFontSizeDip(6));
        }

        var ft = new FormattedText(
            renderText,
            CultureInfo.CurrentCulture,
            flowDirection,
            typeface,
            fontSize,
            textBrush,
            1.0);

        // Mirror GridView.Rendering.cs: Underline/Strikethrough are ordinary WPF TextDecorations;
        // DoubleUnderline is drawn as two manual strokes below (adding it here too would triple it).
        if (CellTextDecorationPlanner.Build(style) is { } decorations)
            ft.SetTextDecorations(decorations);

        // Mirror GridView.Rendering.cs's alignment resolution (hAlign/isNumeric/indentPx feeding
        // CalculateCellTextRenderLayout) for BOTH the rotated and non-rotated branches, so a printed
        // cell's Horizontal/VerticalAlignment and Indent match exactly what's on screen instead of
        // the non-rotated branch hardcoding a flush-left, vertically-centered position regardless of
        // style. isEffectivelyRightToLeft (resolved above) is threaded into both CalculateLayout
        // calls below, so a General-aligned cell on a right-to-left sheet mirrors to the same edge
        // it renders on screen instead of always resolving as if the sheet were left-to-right.
        // Resolved up-front (rather than only just before the layout call below) because the
        // overflow-direction decision right below also needs it.
        var isNumeric = cell.RawValue is NumberValue or DateTimeValue;
        var resolvedHAlign = ResolvePrintedGeneralAlignment(style?.HorizontalAlignment ?? CellHAlign.General, cell.RawValue);
        var indentPx = (style?.IndentLevel ?? 0) * 8.0;

        var canOverflow = !hasOrientation &&
            GridView.CanOverflowCellText(style, cell.RawValue, displayText, merge);
        // Mirror GridView.Rendering.cs's direction-aware overflow: a Right-aligned cell's text is
        // anchored to the cell's right edge, so a too-wide value spills LEFTWARD into empty
        // neighbor cells; Center spills both ways; Left/General (the common case) spills
        // rightward, as before.
        if (canOverflow && ft.Width > maxTextWidth)
        {
            var overflowWidth = resolvedHAlign switch
            {
                CellHAlign.Right => WorksheetPrintCellGeometryPlanner.MeasureOverflowWidth(
                    measurement, pageColumns, colIndex, row, cellLookup, sheet, scanLeft: true),
                CellHAlign.Center => WorksheetPrintCellGeometryPlanner.MeasureOverflowWidth(
                        measurement, pageColumns, colIndex, row, cellLookup, sheet, scanLeft: false)
                    + WorksheetPrintCellGeometryPlanner.MeasureOverflowWidth(
                        measurement, pageColumns, colIndex, row, cellLookup, sheet, scanLeft: true)
                    - measurement.ColumnWidthAt(colIndex),
                _ => WorksheetPrintCellGeometryPlanner.MeasureOverflowWidth(
                    measurement, pageColumns, colIndex, row, cellLookup, sheet, scanLeft: false),
            };
            overflowWidth -= 4;
            if (overflowWidth > maxTextWidth)
                maxTextWidth = overflowWidth;
        }

        ft.MaxTextWidth = Math.Max(1, maxTextWidth);
        // WPF's FormattedText.TextAlignment.Left (the implicit default -- left unset everywhere
        // else in this method) is resolved relative to FlowDirection once MaxTextWidth constrains
        // the layout to a box: for FlowDirection.RightToLeft it means "flush to the reading
        // start," which is physically the RIGHT edge of the box, not the left edge that
        // CalculateLayout below actually resolves textPoint.X to (CalculateLayout always hands
        // back a physical LEFT-edge anchor meant to be drawn flush-left, extending rightward --
        // exactly how the unconstrained LTR case already renders). Left unfixed, an RTL cell's
        // text would silently flip to the box's opposite physical edge regardless of what
        // textPoint.X says. Forcing TextAlignment.Right under RTL flow cancels out WPF's
        // box-relative interpretation so the glyph run still starts flush at the resolved
        // textPoint.X instead of mirroring to the far side of the MaxTextWidth box.
        ft.TextAlignment = isEffectivelyRightToLeft ? TextAlignment.Right : TextAlignment.Left;
        if (wrapText)
        {
            // Allow full multi-line wrapping within the cell width instead of forcing a single
            // ellipsis-truncated line — mirrors GridView.Rendering.cs, which never caps WrapText
            // cells to one line either (the row was already sized to fit the wrapped text).
            ft.Trimming = TextTrimming.None;
        }
        else
        {
            if (!CellTextOrientationLayoutPlanner.IsStackedTextRotation(textRotation))
                ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
        }

        Point textPoint;
        double rotationAngle = 0;
        if (hasOrientation)
        {
            var layout = CellTextOrientationLayoutPlanner.CalculateLayout(
                new CellTextLayoutRect(rect.Left, rect.Top, rect.Width, rect.Height),
                ft.Width,
                ft.Height,
                resolvedHAlign,
                style?.VerticalAlignment,
                isNumeric,
                indentPx,
                textRotation,
                isEffectivelyRightToLeft);
            textPoint = new Point(layout.TextPoint.X, layout.TextPoint.Y);
            rotationAngle = layout.TransformAngle;
        }
        else
        {
            var layout = CellTextOrientationLayoutPlanner.CalculateLayout(
                new CellTextLayoutRect(rect.Left, rect.Top, rect.Width, rect.Height),
                ft.Width,
                ft.Height,
                resolvedHAlign,
                style?.VerticalAlignment,
                isNumeric,
                indentPx,
                textRotation: 0,
                isEffectivelyRightToLeft);
            textPoint = new Point(layout.TextPoint.X, layout.TextPoint.Y);
        }

        // WrapText mirrors GridView.Rendering.cs, which never caps a wrapped cell to one line
        // either -- but when the row wasn't actually resized to fit the wrapped text (FreeX has
        // no automatic row-grow-on-WrapText), the taller-than-the-row FormattedText block must be
        // clipped to the cell's own rect, exactly like CellTextOrientationLayoutPlanner.ShouldClip
        // does for the interactive grid (wrapText && textHeight > clipRect.Height + tolerance), so
        // it doesn't bleed into the row below on the printout.
        var shouldClipWrappedText = wrapText && !hasOrientation && ft.Height > rect.Height + 0.5;
        if (shouldClipWrappedText)
            dc.PushClip(new RectangleGeometry(rect));

        var isRotated = Math.Abs(rotationAngle) > 0.001;
        if (isRotated)
            dc.PushTransform(new RotateTransform(rotationAngle, textPoint.X, textPoint.Y));

        dc.DrawText(ft, textPoint);

        if (style?.DoubleUnderline == true)
        {
            // Mirrors GridView.Rendering.cs's DrawCellText: DoubleUnderline is two manual strokes
            // below the text baseline rather than a WPF TextDecoration (which only draws one line).
            var underlinePen = new Pen(textBrush, 1.0);
            var underlineY = textPoint.Y + ft.Height + 1;
            dc.DrawLine(underlinePen, new Point(textPoint.X, underlineY), new Point(textPoint.X + ft.Width, underlineY));
            dc.DrawLine(underlinePen, new Point(textPoint.X, underlineY + 2), new Point(textPoint.X + ft.Width, underlineY + 2));
        }

        if (isRotated)
            dc.Pop();

        if (shouldClipWrappedText)
            dc.Pop();

        // The single-line ellipsis-bound overlay text only matches what's drawn when the cell is
        // actually capped to one line; a WrapText cell now draws its full paragraph across
        // multiple lines, so its PDF-selectable text should carry the full text too instead of a
        // truncated single-line fragment.
        var overlayText = wrapText
            ? displayText
            : BoundPrintedCellOverlayText(displayText, ft.MaxTextWidth, typeface, fontSize);
        textOverlays.Add(new PdfTextOverlay(
            overlayText,
            textPoint.X,
            textPoint.Y,
            fontSize,
            typeface.FontFamily.Source,
            Bold: style?.Bold == true,
            Italic: style?.Italic == true,
            textColor)
        {
            RotationDegrees = rotationAngle
        });
    }

    /// <summary>
    /// Mirrors GridView.Rendering.cs's ResolveGeneralAlignmentHorizontalAlignment: Excel
    /// General-aligns Boolean and Error cell values to the CENTER (unlike text, which General-aligns
    /// left, and numbers/dates, which General-aligns right -- both already handled by
    /// <see cref="CellTextOrientationLayoutPlanner.CalculateLayout"/>'s own isNumeric flag). Only
    /// applies when the style's alignment actually IS General; an explicit Left/Right/Center/etc.
    /// choice is never overridden by the cell's value type.
    /// </summary>
    private static CellHAlign ResolvePrintedGeneralAlignment(CellHAlign hAlign, ScalarValue? rawValue) =>
        hAlign == CellHAlign.General && rawValue is BoolValue or ErrorValue
            ? CellHAlign.Center
            : hAlign;

    private static Brush ResolvePrintedTextBrush(CellStyle? style, out Color textColor)
    {
        if (style?.FontColor is { } fontColor && !fontColor.IsBlack)
        {
            textColor = Color.FromRgb(fontColor.R, fontColor.G, fontColor.B);
            return new SolidColorBrush(textColor);
        }

        textColor = Colors.Black;
        return Brushes.Black;
    }

    private static void DrawPrintedCellFill(DrawingContext dc, Rect rect, CellStyle style)
    {
        Brush? fill = style.GradientFill is { } gradient
            ? BuildPrintedGradientBrush(gradient)
            : style.FillColor is { } fillColor
                ? new SolidColorBrush(Color.FromRgb(fillColor.R, fillColor.G, fillColor.B))
                : null;

        if (fill is not null)
            dc.DrawRectangle(fill, null, rect);

        if (style.GradientFill is null)
            DrawPrintedFillPattern(dc, rect, style);
    }

    private static Brush BuildPrintedGradientBrush(CellGradientFill gradient)
    {
        if (gradient.Type == CellGradientFillType.Path)
        {
            var originX = gradient.Left + (1.0 - gradient.Left - gradient.Right) / 2.0;
            var originY = gradient.Top + (1.0 - gradient.Top - gradient.Bottom) / 2.0;
            var radial = new RadialGradientBrush
            {
                Center = new Point(originX, originY),
                GradientOrigin = new Point(originX, originY),
                RadiusX = Math.Max(originX, 1.0 - originX),
                RadiusY = Math.Max(originY, 1.0 - originY),
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
            };
            foreach (var stop in gradient.Stops.OrderBy(s => s.Position))
                radial.GradientStops.Add(new GradientStop(Color.FromRgb(stop.Color.R, stop.Color.G, stop.Color.B), stop.Position));
            return radial;
        }

        var radians = gradient.Degree * Math.PI / 180.0;
        var dx = Math.Cos(radians);
        var dy = Math.Sin(radians);
        var start = new Point(0.5 - 0.5 * dx, 0.5 - 0.5 * dy);
        var end = new Point(0.5 + 0.5 * dx, 0.5 + 0.5 * dy);
        var linear = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var stop in gradient.Stops.OrderBy(s => s.Position))
            linear.GradientStops.Add(new GradientStop(Color.FromRgb(stop.Color.R, stop.Color.G, stop.Color.B), stop.Position));
        return linear;
    }

    private static void DrawPrintedFillPattern(DrawingContext dc, Rect rect, CellStyle style)
    {
        if (style.FillPatternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid)
            return;

        var color = style.FillPatternColor ?? CellColor.Black;
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        var pen = new Pen(brush, 0.75);
        const double step = 6;

        dc.PushClip(new RectangleGeometry(rect));
        switch (style.FillPatternStyle)
        {
            case CellFillPatternStyle.Gray0625:
            case CellFillPatternStyle.Gray125:
            case CellFillPatternStyle.LightGray:
            case CellFillPatternStyle.MediumGray:
            case CellFillPatternStyle.DarkGray:
                var opacity = style.FillPatternStyle switch
                {
                    CellFillPatternStyle.Gray0625 => 0.12,
                    CellFillPatternStyle.Gray125 => 0.18,
                    CellFillPatternStyle.LightGray => 0.28,
                    CellFillPatternStyle.MediumGray => 0.45,
                    _ => 0.62
                };
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B)), null, rect);
                break;
            case CellFillPatternStyle.LightHorizontal:
            case CellFillPatternStyle.DarkHorizontal:
                DrawPrintedHorizontalPattern(dc, rect, pen, step);
                break;
            case CellFillPatternStyle.LightVertical:
            case CellFillPatternStyle.DarkVertical:
                DrawPrintedVerticalPattern(dc, rect, pen, step);
                break;
            case CellFillPatternStyle.LightGrid:
            case CellFillPatternStyle.DarkGrid:
                DrawPrintedHorizontalPattern(dc, rect, pen, step);
                DrawPrintedVerticalPattern(dc, rect, pen, step);
                break;
            case CellFillPatternStyle.LightDown:
            case CellFillPatternStyle.DarkDown:
                DrawPrintedDiagonalPattern(dc, rect, pen, descending: true);
                break;
            case CellFillPatternStyle.LightUp:
            case CellFillPatternStyle.DarkUp:
                DrawPrintedDiagonalPattern(dc, rect, pen, descending: false);
                break;
            case CellFillPatternStyle.LightTrellis:
            case CellFillPatternStyle.DarkTrellis:
                DrawPrintedDiagonalPattern(dc, rect, pen, descending: true);
                DrawPrintedDiagonalPattern(dc, rect, pen, descending: false);
                break;
        }
        dc.Pop();
    }

    private static void DrawPrintedHorizontalPattern(DrawingContext dc, Rect rect, Pen pen, double step)
    {
        for (var lineY = rect.Top + step; lineY < rect.Bottom; lineY += step)
            dc.DrawLine(pen, new Point(rect.Left, lineY), new Point(rect.Right, lineY));
    }

    private static void DrawPrintedVerticalPattern(DrawingContext dc, Rect rect, Pen pen, double step)
    {
        for (var lineX = rect.Left + step; lineX < rect.Right; lineX += step)
            dc.DrawLine(pen, new Point(lineX, rect.Top), new Point(lineX, rect.Bottom));
    }

    private static void DrawPrintedDiagonalPattern(DrawingContext dc, Rect rect, Pen pen, bool descending)
    {
        const double step = 8;
        for (var offset = -rect.Height; offset < rect.Width; offset += step)
        {
            var start = descending
                ? new Point(rect.Left + offset, rect.Top)
                : new Point(rect.Left + offset, rect.Bottom);
            var end = descending
                ? new Point(rect.Left + offset + rect.Height, rect.Bottom)
                : new Point(rect.Left + offset + rect.Height, rect.Top);
            dc.DrawLine(pen, start, end);
        }
    }

    private static bool HasVisiblePrintedBorder(CellStyle style) =>
        style.BorderTop.Style != BorderStyle.None ||
        style.BorderBottom.Style != BorderStyle.None ||
        style.BorderLeft.Style != BorderStyle.None ||
        style.BorderRight.Style != BorderStyle.None ||
        style.BorderDiagonalDown.Style != BorderStyle.None ||
        style.BorderDiagonalUp.Style != BorderStyle.None;

    /// <summary>
    /// Draws the printed default-gridline rectangle for a cell, skipping whichever sides are
    /// suppressed because they fall strictly inside a merged region (see the merge-membership
    /// comment in <see cref="DrawPrintedGridCells"/>). A cell fully interior to a merge (all four
    /// sides suppressed) draws nothing at all, matching Excel's printout, which never shows any
    /// interior separator line cutting through a merged range.
    /// </summary>
    private static void DrawPrintedGridlineEdges(
        DrawingContext dc,
        Rect rect,
        Pen pen,
        bool suppressTop,
        bool suppressBottom,
        bool suppressLeft,
        bool suppressRight)
    {
        if (!suppressTop)
            dc.DrawLine(pen, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top));
        if (!suppressBottom)
            dc.DrawLine(pen, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom));
        if (!suppressLeft)
            dc.DrawLine(pen, new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom));
        if (!suppressRight)
            dc.DrawLine(pen, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom));
    }

    private static void DrawPrintedCellBorders(
        DrawingContext dc,
        Rect rect,
        CellStyle style,
        bool blackAndWhite,
        bool suppressTop,
        bool suppressBottom,
        bool suppressLeft,
        bool suppressRight,
        CellBorder topBorder,
        CellBorder bottomBorder,
        CellBorder leftBorder,
        CellBorder rightBorder,
        bool drawDiagonal,
        double diagonalWidth,
        double diagonalHeight)
    {
        if (!suppressTop)
            DrawPrintedBorderEdge(dc, topBorder, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top), blackAndWhite);
        if (!suppressBottom)
            DrawPrintedBorderEdge(dc, bottomBorder, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom), blackAndWhite);
        if (!suppressLeft)
            DrawPrintedBorderEdge(dc, leftBorder, new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom), blackAndWhite);
        if (!suppressRight)
            DrawPrintedBorderEdge(dc, rightBorder, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom), blackAndWhite);
        if (drawDiagonal && style.BorderDiagonalDown.Style != BorderStyle.None)
            DrawPrintedBorderEdge(dc, style.BorderDiagonalDown, new Point(rect.Left, rect.Top), new Point(rect.Left + diagonalWidth, rect.Top + diagonalHeight), blackAndWhite);
        if (drawDiagonal && style.BorderDiagonalUp.Style != BorderStyle.None)
            DrawPrintedBorderEdge(dc, style.BorderDiagonalUp, new Point(rect.Left, rect.Top + diagonalHeight), new Point(rect.Left + diagonalWidth, rect.Top), blackAndWhite);
    }

    private static void DrawPrintedBorderEdge(DrawingContext dc, CellBorder border, Point p1, Point p2, bool blackAndWhite = false)
    {
        if (border.Style == BorderStyle.None) return;

        double thickness = border.Style switch
        {
            BorderStyle.Hair => 0.25,
            BorderStyle.Thin => 0.5,
            BorderStyle.Medium or BorderStyle.MediumDashed or BorderStyle.MediumDashDot or BorderStyle.MediumDashDotDot or BorderStyle.SlantDashDot => 1.5,
            BorderStyle.Thick => 2.5,
            _ => 0.5
        };

        DashStyle dash = border.Style switch
        {
            BorderStyle.Dashed or BorderStyle.MediumDashed => DashStyles.Dash,
            BorderStyle.Dotted => DashStyles.Dot,
            BorderStyle.DashDot or BorderStyle.MediumDashDot => DashStyles.DashDot,
            BorderStyle.DashDotDot or BorderStyle.MediumDashDotDot => DashStyles.DashDotDot,
            BorderStyle.SlantDashDot => DashStyles.DashDot,
            BorderStyle.Hair => DashStyles.Solid,
            _ => DashStyles.Solid
        };

        // Excel's "Black and white" print option forces every border to solid black regardless of
        // its authored color.
        var borderBrush = blackAndWhite
            ? Brushes.Black
            : new SolidColorBrush(Color.FromRgb(border.Color.R, border.Color.G, border.Color.B));
        var pen = new Pen(borderBrush, thickness)
        {
            DashStyle = dash
        };

        if (border.Style == BorderStyle.Double)
        {
            DrawPrintedDoubleBorderLines(dc, pen, p1, p2);
            return;
        }

        dc.DrawLine(pen, p1, p2);
    }

    private static void DrawPrintedDoubleBorderLines(DrawingContext dc, Pen pen, Point p1, Point p2)
    {
        const double gap = 1.0;

        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6)
        {
            dc.DrawLine(pen, p1, p2);
            return;
        }

        var offsetX = -dy / length * (gap / 2.0);
        var offsetY = dx / length * (gap / 2.0);

        dc.DrawLine(pen, new Point(p1.X + offsetX, p1.Y + offsetY), new Point(p2.X + offsetX, p2.Y + offsetY));
        dc.DrawLine(pen, new Point(p1.X - offsetX, p1.Y - offsetY), new Point(p2.X - offsetX, p2.Y - offsetY));
    }
}
