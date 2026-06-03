using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    private static readonly Typeface PrintedChartTypeface = new("Segoe UI");
    private static readonly Typeface PrintedTextBoxTypeface = new("Segoe UI");

    private static void DrawPrintedCharts(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        ViewportModel viewport,
        IReadOnlyList<ChartModel> charts,
        WorkbookTheme workbookTheme,
        IReadOnlyList<uint> bodyRows,
        IReadOnlyList<uint> bodyColumns,
        int rowPlanTitleCount,
        int columnPlanTitleCount,
        double gridLeft,
        double gridTop,
        double colWidth,
        double rowHeight)
    {
        if (charts.Count == 0 || bodyRows.Count == 0 || bodyColumns.Count == 0)
            return;

        var bodyGridLeft = gridLeft + columnPlanTitleCount * colWidth;
        var bodyGridTop = gridTop + rowPlanTitleCount * rowHeight;
        var bodyGridRect = new Rect(
            bodyGridLeft,
            bodyGridTop,
            bodyColumns.Count * colWidth,
            bodyRows.Count * rowHeight);
        var firstBodyColumn = bodyColumns[0];
        var firstBodyRow = bodyRows[0];
        var pageGridLeft = (firstBodyColumn - 1) * colWidth;
        var pageGridTop = (firstBodyRow - 1) * rowHeight;
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
                AddPrintedChartTextOverlays(textOverlays, chart, workbookTheme, chartRect);
        }
        dc.Pop();
    }

    private static void AddPrintedChartTextOverlays(
        ICollection<PdfTextOverlay> textOverlays,
        ChartModel chart,
        WorkbookTheme workbookTheme,
        Rect chartRect)
    {
        var textInset = Math.Min(8, Math.Max(0, chartRect.Width / 20));
        AddPrintedChartCenteredOverlay(
            textOverlays,
            chart.Title,
            chartRect.Left + chartRect.Width / 2,
            chartRect.Top + textInset,
            Math.Max(1, chartRect.Width - textInset * 2),
            NormalizePrintedChartFontSize(chart.ChartTitleFontSize, 16),
            ResolveChartTitleOverlayColor(chart, workbookTheme),
            rotationDegrees: 0);

        if (!ChartTypeSupport.SupportsAxes(chart.Type))
            return;

        var axisFontSize = NormalizePrintedChartFontSize(chart.AxisTitleFontSize, 12);
        var axisColor = ResolveAxisTitleOverlayColor(chart, workbookTheme);
        if (!chart.HideXAxis)
        {
            AddPrintedChartCenteredOverlay(
                textOverlays,
                chart.XAxisTitle,
                chartRect.Left + chartRect.Width / 2,
                chartRect.Bottom - axisFontSize - textInset,
                Math.Max(1, chartRect.Width - textInset * 2),
                axisFontSize,
                axisColor,
                rotationDegrees: 0);
        }

        if (!chart.HideYAxis)
        {
            AddPrintedChartVerticalAxisOverlay(
                textOverlays,
                chart.YAxisTitle,
                chartRect.Left + textInset,
                chartRect.Top + chartRect.Height / 2,
                Math.Max(1, chartRect.Height - textInset * 2),
                axisFontSize,
                axisColor);
        }
    }

    private static void AddPrintedChartCenteredOverlay(
        ICollection<PdfTextOverlay> textOverlays,
        string? text,
        double centerX,
        double y,
        double maxWidth,
        double fontSize,
        CellColor color,
        double rotationDegrees)
    {
        var bounded = BoundPrintedChartOverlayText(text, maxWidth, fontSize);
        if (bounded.Length == 0)
            return;

        var textWidth = MeasurePrintedChartText(bounded, fontSize).WidthIncludingTrailingWhitespace;
        var x = centerX - textWidth / 2;
        textOverlays.Add(CreatePrintedChartTextOverlay(bounded, x, y, fontSize, color, rotationDegrees));
    }

    private static void AddPrintedChartVerticalAxisOverlay(
        ICollection<PdfTextOverlay> textOverlays,
        string? text,
        double x,
        double centerY,
        double maxWidth,
        double fontSize,
        CellColor color)
    {
        var bounded = BoundPrintedChartOverlayText(text, maxWidth, fontSize);
        if (bounded.Length == 0)
            return;

        var textWidth = MeasurePrintedChartText(bounded, fontSize).WidthIncludingTrailingWhitespace;
        textOverlays.Add(CreatePrintedChartTextOverlay(
            bounded,
            x,
            centerY + textWidth / 2 - fontSize,
            fontSize,
            color,
            rotationDegrees: -90));
    }

    private static PdfTextOverlay CreatePrintedChartTextOverlay(
        string text,
        double x,
        double y,
        double fontSize,
        CellColor color,
        double rotationDegrees) =>
        new(
            text,
            x,
            y,
            fontSize,
            PrintedChartTypeface.FontFamily.Source,
            Bold: false,
            Italic: false,
            Color.FromRgb(color.R, color.G, color.B))
        {
            RotationDegrees = rotationDegrees
        };

    private static string BoundPrintedChartOverlayText(string? text, double maxWidth, double fontSize)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        const string ellipsis = "\u2026";
        var boundedWidth = Math.Max(1, maxWidth);
        var candidate = text.Trim().TrimEnd();
        if (FitsPrintedChartVisibleWidth(candidate, boundedWidth, fontSize))
            return candidate;

        while (candidate.Length > 0 && !FitsPrintedChartOverlayWidth(candidate + ellipsis, boundedWidth, fontSize))
            candidate = candidate[..^1].TrimEnd();

        return candidate.Length == 0 ? ellipsis : candidate + ellipsis;
    }

    private static bool FitsPrintedChartVisibleWidth(string text, double maxWidth, double fontSize) =>
        MeasurePrintedChartText(text, fontSize).Width <= Math.Max(1, maxWidth);

    private static bool FitsPrintedChartOverlayWidth(string text, double maxWidth, double fontSize) =>
        MeasurePrintedChartText(text, fontSize).WidthIncludingTrailingWhitespace <= Math.Max(1, maxWidth);

    private static FormattedText MeasurePrintedChartText(string text, double fontSize) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            PrintedChartTypeface,
            fontSize,
            Brushes.Black,
            1.0);

    private static double NormalizePrintedChartFontSize(double fontSize, double fallback) =>
        double.IsFinite(fontSize) && fontSize > 0 ? fontSize : fallback;

    private static CellColor ResolveChartTitleOverlayColor(ChartModel chart, WorkbookTheme workbookTheme) =>
        chart.ResolveChartTitleTextColor(workbookTheme) ??
        ResolveChartDefaultOverlayColor(chart, workbookTheme);

    private static CellColor ResolveAxisTitleOverlayColor(ChartModel chart, WorkbookTheme workbookTheme) =>
        chart.ResolveAxisTitleTextColor(workbookTheme) ??
        ResolveChartDefaultOverlayColor(chart, workbookTheme);

    private static CellColor ResolveChartDefaultOverlayColor(ChartModel chart, WorkbookTheme workbookTheme) =>
        chart.ChartDefaultTextThemeColor?.Resolve(workbookTheme) ??
        chart.ChartDefaultTextColor ??
        CellColor.Black;

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
        IReadOnlyList<TextBoxModel> textBoxes,
        WorkbookTheme workbookTheme,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        double gridLeft,
        double gridTop,
        double colWidth,
        double rowHeight)
    {
        if (textBoxes.Count == 0)
            return;

        foreach (var textBox in textBoxes)
        {
            if (!textBox.IsVisible)
                continue;

            var rowIndex = IndexOf(pageRows, textBox.Anchor.Row);
            var columnIndex = IndexOf(pageColumns, textBox.Anchor.Col);
            if (rowIndex < 0 || columnIndex < 0)
                continue;

            var rect = new Rect(
                gridLeft + columnIndex * colWidth,
                gridTop + rowIndex * rowHeight,
                Math.Max(24, textBox.Width),
                Math.Max(18, textBox.Height));
            var fill = textBox.GetEffectiveFillColor(workbookTheme, CellColor.White);
            var outline = textBox.GetEffectiveOutlineColor(workbookTheme, new CellColor(89, 89, 89));
            var fillBrush = CreateFrozenBrush(fill, 242);
            var outlinePen = new Pen(CreateFrozenBrush(outline), 1);
            outlinePen.Freeze();
            dc.DrawRectangle(fillBrush, outlinePen, rect);

            if (string.IsNullOrEmpty(textBox.Text))
                continue;

            var textRect = new Rect(rect.Left + 4, rect.Top + 4, Math.Max(1, rect.Width - 8), Math.Max(1, rect.Height - 8));
            DrawPrintedTextBoxText(dc, textBox.Text, textRect);
            AddPrintedTextBoxOverlays(textOverlays, textBox.Text, textRect);
        }
    }

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

    private static int IndexOf(IReadOnlyList<uint> values, uint value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] == value)
                return index;
        }

        return -1;
    }

    private static SolidColorBrush CreateFrozenBrush(CellColor color, byte alpha = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
