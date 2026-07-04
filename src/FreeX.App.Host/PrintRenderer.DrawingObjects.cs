using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    private static readonly Typeface PrintedTextBoxTypeface = new("Segoe UI");

    private static void DrawPrintedCharts(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        ViewportModel viewport,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> pageCellLookup,
        Sheet sheet,
        IReadOnlyList<ChartModel> charts,
        WorkbookTheme workbookTheme,
        IReadOnlyList<uint> bodyRows,
        IReadOnlyList<uint> bodyColumns,
        int rowPlanTitleCount,
        int columnPlanTitleCount,
        double gridLeft,
        double gridTop,
        PrintGridMeasurement measurement)
    {
        if (charts.Count == 0 || bodyRows.Count == 0 || bodyColumns.Count == 0)
            return;

        var bodyGridLeft = gridLeft + measurement.ColumnOffset(columnPlanTitleCount);
        var bodyGridTop = gridTop + measurement.RowOffset(rowPlanTitleCount);
        var bodyGridRect = new Rect(
            bodyGridLeft,
            bodyGridTop,
            measurement.ColumnOffset(columnPlanTitleCount + bodyColumns.Count) - measurement.ColumnOffset(columnPlanTitleCount),
            measurement.RowOffset(rowPlanTitleCount + bodyRows.Count) - measurement.RowOffset(rowPlanTitleCount));

        // chart.Left/chart.Top are absolute pixel offsets from the sheet's real (non-uniform,
        // hidden-row/column-skipping) origin — see XlsxDrawingAnchorApplier. Translate them into this
        // page's grid coordinates by subtracting the real-sheet pixel offset of the page's first body
        // row/column, matching the coordinate space anchors were computed in.
        var firstBodyColumn = bodyColumns[0];
        var firstBodyRow = bodyRows[0];
        var pageGridLeft = ChartAnchorGeometry.SumColumnPixels(sheet, 1, firstBodyColumn - 1);
        var pageGridTop = ChartAnchorGeometry.SumRowPixels(sheet, 1, firstBodyRow - 1);
        var pageGridRect = new Rect(
            pageGridLeft,
            pageGridTop,
            bodyGridRect.Width,
            bodyGridRect.Height);

        dc.PushClip(new RectangleGeometry(bodyGridRect));
        foreach (var chart in charts)
        {
            if (!ShouldPrintChart(chart, pageGridRect))
                continue;

            var image = ChartRenderer.Render(chart, viewport, workbookTheme);
            if (image is null)
                continue;

            var chartRect = new Rect(
                bodyGridLeft + chart.Left - pageGridLeft,
                bodyGridTop + chart.Top - pageGridTop,
                chart.Width,
                chart.Height);
            dc.DrawImage(image, chartRect);
            if (bodyGridRect.Contains(chartRect))
                AddPrintedChartTextOverlays(textOverlays, chart, workbookTheme, chartRect, viewport, pageCellLookup);
        }
        dc.Pop();
    }

    private static bool ShouldPrintChart(ChartModel chart, Rect pageGridRect)
    {
        if (!chart.IsVisible ||
            !double.IsFinite(chart.Left) ||
            !double.IsFinite(chart.Top) ||
            !double.IsFinite(chart.Width) ||
            !double.IsFinite(chart.Height) ||
            chart.Width <= 0 ||
            chart.Height <= 0)
        {
            return false;
        }

        var chartRect = new Rect(chart.Left, chart.Top, chart.Width, chart.Height);
        return chartRect.IntersectsWith(pageGridRect);
    }

    private static void DrawPrintedTextBoxes(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        IReadOnlyList<PageTextBoxBlock> textBoxes)
    {
        if (textBoxes.Count == 0)
            return;

        foreach (var textBox in textBoxes)
        {
            var rect = ToRect(textBox.Bounds);
            var fillBrush = textBox.Fill is { } fill ? CreateFrozenBrush(fill, textBox.FillAlpha) : null;
            var outlinePen = new Pen(CreateFrozenBrush(textBox.Outline), textBox.OutlineThickness);
            outlinePen.Freeze();
            dc.DrawRectangle(fillBrush, outlinePen, rect);

            if (string.IsNullOrEmpty(textBox.Text))
                continue;

            var textRect = ToRect(textBox.TextBounds);
            DrawPrintedTextBoxText(dc, textBox.Text, textRect);
            AddPrintedTextBoxOverlays(textOverlays, textBox.Text, textRect);
        }
    }

    private static Rect ToRect(LayoutRect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static void DrawPrintedTextBoxText(DrawingContext dc, string text, Rect textRect)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            PrintedTextBoxTypeface,
            PrintFontSize,
            Brushes.Black,
            1.0)
        {
            MaxTextWidth = textRect.Width,
            MaxTextHeight = textRect.Height,
            Trimming = TextTrimming.CharacterEllipsis
        };

        dc.PushClip(new RectangleGeometry(textRect));
        dc.DrawText(formattedText, textRect.TopLeft);
        dc.Pop();
    }

    private static void AddPrintedTextBoxOverlays(
        ICollection<PdfTextOverlay> textOverlays,
        string text,
        Rect textRect)
    {
        var lineHeight = MeasurePrintedTextBoxText("Ag").Height;
        var maxLines = Math.Max(1, (int)Math.Floor(textRect.Height / Math.Max(1, lineHeight)));
        var lineIndex = 0;
        foreach (var line in WrapPrintedTextBoxOverlayText(text, textRect.Width, maxLines))
        {
            textOverlays.Add(new PdfTextOverlay(
                line,
                textRect.Left,
                textRect.Top + lineHeight * lineIndex,
                PrintFontSize,
                PrintedTextBoxTypeface.FontFamily.Source,
                Bold: false,
                Italic: false,
                Colors.Black));
            lineIndex++;
        }
    }

    private static IReadOnlyList<string> WrapPrintedTextBoxOverlayText(
        string text,
        double maxWidth,
        int maxLines)
    {
        var lines = new List<string>();
        var hardLines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var truncated = false;
        for (var hardLineIndex = 0; hardLineIndex < hardLines.Length && lines.Count < maxLines && !truncated; hardLineIndex++)
            truncated = AddWrappedPrintedTextBoxHardLine(lines, hardLines[hardLineIndex], maxWidth, maxLines);

        if (lines.Count > 0 &&
            !lines[^1].EndsWith("\u2026", StringComparison.Ordinal) &&
            (truncated || lines.Count == maxLines && ProducesMorePrintedTextBoxLines(text, lines.Count, maxWidth, maxLines)))
        {
            lines[^1] = TrimPrintedTextBoxOverlayText(lines[^1], maxWidth);
        }

        return lines;
    }

    private static bool AddWrappedPrintedTextBoxHardLine(
        ICollection<string> lines,
        string hardLine,
        double maxWidth,
        int maxLines)
    {
        var words = hardLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            if (lines.Count < maxLines)
                lines.Add("");
            return false;
        }

        var index = 0;
        while (index < words.Length && lines.Count < maxLines)
        {
            var line = words[index++];
            while (index < words.Length && FitsPrintedTextBoxWidth($"{line} {words[index]}", maxWidth))
                line = $"{line} {words[index++]}";

            if (!FitsPrintedTextBoxWidth(line, maxWidth))
            {
                lines.Add(TrimPrintedTextBoxOverlayText(line, maxWidth));
                return true;
            }

            lines.Add(line);
        }

        return index < words.Length;
    }

    private static bool ProducesMorePrintedTextBoxLines(string text, int emittedLineCount, double maxWidth, int maxLines)
    {
        var replay = new List<string>();
        var hardLines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        foreach (var hardLine in hardLines)
        {
            AddWrappedPrintedTextBoxHardLine(replay, hardLine, maxWidth, int.MaxValue);
            if (replay.Count > maxLines)
                return true;
        }

        return replay.Count > emittedLineCount;
    }

    private static bool FitsPrintedTextBoxWidth(string text, double maxWidth) =>
        MeasurePrintedTextBoxText(text).WidthIncludingTrailingWhitespace <= Math.Max(1, maxWidth);

    private static string TrimPrintedTextBoxOverlayText(string text, double maxWidth)
    {
        const string ellipsis = "\u2026";
        var candidate = text.TrimEnd();
        while (candidate.Length > 0 && !FitsPrintedTextBoxWidth(candidate + ellipsis, maxWidth))
            candidate = candidate[..^1].TrimEnd();

        return candidate.Length == 0 ? ellipsis : candidate + ellipsis;
    }

    private static FormattedText MeasurePrintedTextBoxText(string text) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            PrintedTextBoxTypeface,
            PrintFontSize,
            Brushes.Black,
            1.0);

    private static SolidColorBrush CreateFrozenBrush(PresentationRgb color, byte alpha = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
