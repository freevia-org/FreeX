using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static partial class PrintRenderer
{
    private static readonly Typeface PrintedTextBoxTypeface = new("Segoe UI");

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
