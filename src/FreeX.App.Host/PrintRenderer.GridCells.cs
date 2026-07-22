using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
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
    // Page Setup > Sheet > "Black and white" (Sheet.PrintBlackAndWhite) is threaded in here the
    // same way RenderPageVisual (PrintRenderer.HeaderFooter.cs) already threads it to
    // DrawDisplayedComments: fills are suppressed (no color/pattern paint), and borders/gridlines
    // are forced to solid black instead of their authored/light-gray color, matching Excel's
    // grayscale print preview.
    private static readonly Pen BlackAndWhiteGridlinePen = new(Brushes.Black, 0.5);

    private static void DrawPrintedGridCells(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        ICollection<PdfLinkOverlay> linkOverlays,
        ICollection<PdfCellDestinationOverlay> cellDestinationOverlays,
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyDictionary<(uint Row, uint Col), PdfLinkTarget> hyperlinkLookup,
        IReadOnlyDictionary<(uint Row, uint Col), CellAddress> cellDestinationLookup,
        bool printGridlines,
        WorksheetPrintErrorValue printErrorValue,
        double gridLeft,
        double gridTop,
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
                if (!blackAndWhite && style is not null && HasVisiblePrintedFill(style))
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
                    DrawPrintedCellBorders(dc, cellRect, style, blackAndWhite, suppressTop, suppressBottom, suppressLeft, suppressRight);
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
                    cellRect,
                    measurement,
                    pageColumns,
                    colIndex,
                    row,
                    cellLookup,
                    blackAndWhite);
            }
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
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            textBrush,
            1.0);

        // Mirror GridView.Rendering.cs: Underline/Strikethrough are ordinary WPF TextDecorations;
        // DoubleUnderline is drawn as two manual strokes below (adding it here too would triple it).
        if (CellTextDecorationPlanner.Build(style) is { } decorations)
            ft.SetTextDecorations(decorations);

        var canOverflow = !hasOrientation &&
            GridView.CanOverflowCellText(style, cell.RawValue, displayText, merge: null);
        if (canOverflow && ft.Width > maxTextWidth)
        {
            var overflowWidth = ComputePrintedOverflowWidth(measurement, pageColumns, colIndex, row, cellLookup) - 4;
            if (overflowWidth > maxTextWidth)
                maxTextWidth = overflowWidth;
        }

        ft.MaxTextWidth = Math.Max(1, maxTextWidth);
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

        // Mirror GridView.Rendering.cs's alignment resolution (hAlign/isNumeric/indentPx feeding
        // CalculateCellTextRenderLayout) for BOTH the rotated and non-rotated branches, so a printed
        // cell's Horizontal/VerticalAlignment and Indent match exactly what's on screen instead of
        // the non-rotated branch hardcoding a flush-left, vertically-centered position regardless of
        // style. (Reading-order/RTL mirroring is not threaded here: DrawPrintedGridCells's caller,
        // PrintRenderer.HeaderFooter.cs, is owned by a different fix bucket and doesn't pass the
        // sheet's IsRightToLeft flag down to this method.)
        var isNumeric = cell.RawValue is NumberValue or DateTimeValue;
        var resolvedHAlign = ResolvePrintedGeneralAlignment(style?.HorizontalAlignment ?? CellHAlign.General, cell.RawValue);
        var indentPx = (style?.IndentLevel ?? 0) * 8.0;

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
                textRotation);
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
                textRotation: 0);
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
    /// Extends the available draw width for a cell's text into consecutive blank columns to its
    /// right on the same printed page — mirroring GridView.Rendering.cs's overflow logic — so long
    /// unwrapped text spills across empty neighbor cells on the printout instead of being hard-cut
    /// with an ellipsis at its own column boundary.
    /// </summary>
    private static double ComputePrintedOverflowWidth(
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageColumns,
        int colIndex,
        uint row,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup)
    {
        var width = measurement.ColumnWidthAt(colIndex);
        var nextIndex = colIndex + 1;
        while (nextIndex < pageColumns.Count)
        {
            var nextCol = pageColumns[nextIndex];
            if (cellLookup.TryGetValue((row, nextCol), out var nextCell) && !string.IsNullOrEmpty(nextCell.DisplayText))
                break;

            width += measurement.ColumnWidthAt(nextIndex);
            nextIndex++;
        }

        return width;
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

    private static bool HasVisiblePrintedFill(CellStyle style) =>
        style.FillColor.HasValue ||
        style.FillPatternStyle != CellFillPatternStyle.None ||
        style.GradientFill is not null;

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
        bool blackAndWhite = false,
        bool suppressTop = false,
        bool suppressBottom = false,
        bool suppressLeft = false,
        bool suppressRight = false)
    {
        if (!suppressTop)
            DrawPrintedBorderEdge(dc, style.BorderTop, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top), blackAndWhite);
        if (!suppressBottom)
            DrawPrintedBorderEdge(dc, style.BorderBottom, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom), blackAndWhite);
        if (!suppressLeft)
            DrawPrintedBorderEdge(dc, style.BorderLeft, new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom), blackAndWhite);
        if (!suppressRight)
            DrawPrintedBorderEdge(dc, style.BorderRight, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom), blackAndWhite);
        if (style.BorderDiagonalDown.Style != BorderStyle.None)
            DrawPrintedBorderEdge(dc, style.BorderDiagonalDown, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Bottom), blackAndWhite);
        if (style.BorderDiagonalUp.Style != BorderStyle.None)
            DrawPrintedBorderEdge(dc, style.BorderDiagonalUp, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Top), blackAndWhite);
    }

    private static void DrawPrintedBorderEdge(DrawingContext dc, CellBorder border, Point p1, Point p2, bool blackAndWhite = false)
    {
        if (border.Style == BorderStyle.None) return;

        double thickness = border.Style switch
        {
            BorderStyle.Hair => 0.25,
            BorderStyle.Thin => 0.5,
            BorderStyle.Medium or BorderStyle.MediumDashed or BorderStyle.MediumDashDot or BorderStyle.MediumDashDotDot => 1.5,
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
