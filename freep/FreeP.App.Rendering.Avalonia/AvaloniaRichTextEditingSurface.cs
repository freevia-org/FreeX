using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Media.Imaging;
using Avalonia.Utilities;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// Draws rich editor glyphs, selection, and caret from one set of TextLayout objects.
/// Logical offsets remain model offsets, matching the WPF editing document.
/// </summary>
internal sealed class AvaloniaRichTextEditingSurface : Control
{
    private const double PtToDip = 96.0 / 72.0;
    private const double CaretWidth = 1.25;
    private static readonly Thickness ContentPadding = new(4, 3, 4, 3);
    private static readonly IBrush DefaultForeground = Brushes.Black;
    private static readonly IBrush SelectionBrush =
        new SolidColorBrush(Color.FromArgb(0x78, 0xAD, 0xD6, 0xFF));
    private static readonly IPen CaretPen = new Pen(Brushes.Black, CaretWidth);

    private readonly List<ParagraphLayout> _layouts = [];
    private InCanvasRichTextVisualPlan _plan = InCanvasRichTextVisualPlanner.Create(null);
    private string _fallbackFontFamily = InCanvasRichTextEditorDefaults.FallbackFontFamily;
    private double _fallbackFontSizePt = InCanvasRichTextEditorDefaults.ShapeFallbackFontSizePt;
    private double _layoutWidth = double.NaN;
    private int _selectionStart;
    private int _selectionEnd;
    private bool _showCaret;

    internal AvaloniaRichTextEditingSurface()
    {
        IsHitTestVisible = false;
        DetachedFromVisualTree += (_, _) => DisposeLayouts();
    }

    internal InCanvasRichTextVisualPlan VisualPlan => _plan;

    internal string FallbackFontFamily => _fallbackFontFamily;

    internal double FallbackFontSizePt => _fallbackFontSizePt;

    internal IReadOnlyList<Rect> SelectionRects
    {
        get
        {
            EnsureLayouts();
            return BuildSelectionRects();
        }
    }

    internal Rect CaretRect
    {
        get
        {
            EnsureLayouts();
            return BuildCaretRect();
        }
    }

    internal void UpdateBody(
        TextBody body,
        string? fallbackFontFamily,
        double? fallbackFontSizePt)
    {
        _plan = InCanvasRichTextVisualPlanner.Create(body);
        if (!string.IsNullOrWhiteSpace(fallbackFontFamily))
            _fallbackFontFamily = fallbackFontFamily;
        if (fallbackFontSizePt is > 0)
            _fallbackFontSizePt = fallbackFontSizePt.Value;
        InvalidateLayouts();
    }

    internal void UpdateSelection(int start, int end, bool showCaret)
    {
        int length = _plan.PlainText.Length;
        _selectionStart = Math.Clamp(start, 0, length);
        _selectionEnd = Math.Clamp(end, 0, length);
        _showCaret = showCaret;
        InvalidateVisual();
    }

    internal int HitTestLogicalPosition(Point point)
    {
        EnsureLayouts();
        if (_layouts.Count == 0)
            return 0;

        var item = _layouts.FirstOrDefault(candidate => point.Y < candidate.Bottom)
            ?? _layouts[^1];
        var localPoint = new Point(
            Math.Max(0, point.X - item.Origin.X),
            Math.Max(0, point.Y - item.Origin.Y));
        var hit = item.Layout.HitTestPoint(localPoint);
        int localTextPosition = Math.Clamp(
            hit.TextPosition,
            0,
            item.Paragraph.Text.Length);
        return item.Paragraph.GlobalStart + localTextPosition;
    }

    internal int MoveCaretVertically(int logicalPosition, int lineDelta)
    {
        EnsureLayouts();
        if (_layouts.Count == 0 || lineDelta == 0)
            return Math.Clamp(logicalPosition, 0, _plan.PlainText.Length);

        int paragraphIndex = FindParagraphIndex(logicalPosition);
        var current = _layouts[paragraphIndex];
        int displayPosition = ToDisplayPosition(current.Paragraph, logicalPosition);
        int lineIndex = current.Layout.GetLineIndexFromCharacterIndex(displayPosition, trailingEdge: false);
        var caret = current.Layout.HitTestTextPosition(displayPosition);
        int targetLine = lineIndex + Math.Sign(lineDelta);
        int targetParagraph = paragraphIndex;

        if (targetLine < 0)
        {
            if (targetParagraph == 0)
                return 0;
            targetParagraph--;
            targetLine = _layouts[targetParagraph].Layout.TextLines.Count - 1;
        }
        else if (targetLine >= current.Layout.TextLines.Count)
        {
            if (targetParagraph + 1 >= _layouts.Count)
                return _plan.PlainText.Length;
            targetParagraph++;
            targetLine = 0;
        }

        var target = _layouts[targetParagraph];
        double targetY = target.Layout.TextLines
            .Take(targetLine)
            .Sum(line => line.Height)
            + target.Layout.TextLines[targetLine].Height / 2;
        var hit = target.Layout.HitTestPoint(new Point(caret.X, targetY));
        int local = Math.Clamp(
            hit.TextPosition,
            0,
            target.Paragraph.Text.Length);
        return target.Paragraph.GlobalStart + local;
    }

    internal int MoveCaretToVisualLineBoundary(int logicalPosition, bool end)
    {
        EnsureLayouts();
        if (_layouts.Count == 0)
            return 0;

        var item = _layouts[FindParagraphIndex(logicalPosition)];
        int displayPosition = ToDisplayPosition(item.Paragraph, logicalPosition);
        int lineIndex = item.Layout.GetLineIndexFromCharacterIndex(displayPosition, trailingEdge: end);
        var line = item.Layout.TextLines[lineIndex];
        int displayBoundary = end
            ? line.FirstTextSourceIndex + line.Length - line.NewLineLength
            : line.FirstTextSourceIndex;
        int local = Math.Clamp(
            displayBoundary,
            0,
            item.Paragraph.Text.Length);
        return item.Paragraph.GlobalStart + local;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureLayouts();

        foreach (var rect in BuildSelectionRects())
            context.FillRectangle(SelectionBrush, rect);

        foreach (var item in _layouts)
        {
            if (item.BulletLayout is not null)
                item.BulletLayout.Draw(context, item.BulletOrigin);
            else if (item.BulletImage is not null)
            {
                double size = Math.Max(1, ToDip(item.Paragraph.BulletFontSizePt ?? _fallbackFontSizePt));
                context.DrawImage(item.BulletImage, new Rect(item.BulletOrigin, new Size(size, size)));
            }

            item.Layout.Draw(context, item.Origin);
        }

        var caret = BuildCaretRect();
        if (caret.Width > 0 && caret.Height > 0)
        {
            context.DrawLine(
                CaretPen,
                new Point(caret.X, caret.Y),
                new Point(caret.X, caret.Bottom));
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 320;
        double height = double.IsFinite(availableSize.Height) ? availableSize.Height : 90;
        return new Size(Math.Max(1, width), Math.Max(1, height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!AreClose(_layoutWidth, finalSize.Width))
            InvalidateLayouts();
        return finalSize;
    }

    private void EnsureLayouts()
    {
        double width = Math.Max(1, Bounds.Width);
        if (_layouts.Count > 0 && AreClose(_layoutWidth, width))
            return;

        DisposeLayouts();
        _layoutWidth = width;
        double y = ContentPadding.Top;

        foreach (var paragraph in _plan.Paragraphs)
        {
            y += paragraph.SpaceBeforeDip;
            double originX = ContentPadding.Left + paragraph.IndentDip;
            double maxWidth = Math.Max(
                1,
                width - originX - ContentPadding.Right);
            var layout = CreateLayout(paragraph, maxWidth);
            var origin = new Point(originX, y);
            var bulletOrigin = new Point(
                ContentPadding.Left + paragraph.IndentDip - paragraph.HangingDip,
                y);
            _layouts.Add(new ParagraphLayout(
                paragraph,
                layout,
                origin,
                CreateBulletLayout(paragraph),
                bulletOrigin,
                CreateBulletImage(paragraph)));
            y += layout.Height + paragraph.SpaceAfterDip;
        }
    }

    private TextLayout CreateLayout(InCanvasRichTextVisualParagraph paragraph, double maxWidth)
    {
        var seed = paragraph.Runs.FirstOrDefault();
        var defaultTypeface = CreateTypeface(seed);
        double defaultFontSize = ToDip(seed?.FontSizePt ?? _fallbackFontSizePt);
        var overrides = new List<ValueSpan<TextRunProperties>>();

        foreach (var run in paragraph.Runs)
        {
            if (run.Length == 0)
                continue;
            overrides.Add(new ValueSpan<TextRunProperties>(
                run.Start,
                run.Length,
                CreateRunProperties(run)));
        }

        return new TextLayout(
            paragraph.Text,
            defaultTypeface,
            defaultFontSize,
            DefaultForeground,
            ToAvaloniaAlignment(paragraph.Alignment),
            TextWrapping.Wrap,
            TextTrimming.None,
            textDecorations: null,
            FlowDirection.LeftToRight,
            maxWidth,
            double.PositiveInfinity,
            lineHeight: double.NaN,
            letterSpacing: 0,
            maxLines: 0,
            fontFeatures: null,
            textStyleOverrides: overrides);
    }

    private TextLayout? CreateBulletLayout(InCanvasRichTextVisualParagraph paragraph)
    {
        if (paragraph.BulletText.Length == 0 || paragraph.BulletKind == BulletKind.Image)
            return null;

        var typeface = new Typeface(
            new FontFamily(string.IsNullOrWhiteSpace(paragraph.BulletFontFamily)
                ? _fallbackFontFamily
                : paragraph.BulletFontFamily),
            FontStyle.Normal,
            FontWeight.Normal);
        IBrush foreground = paragraph.BulletColor is { } color
            ? new SolidColorBrush(Color.FromRgb(color.Resolved.R, color.Resolved.G, color.Resolved.B))
            : DefaultForeground;
        return new TextLayout(
            paragraph.BulletText,
            typeface,
            ToDip(paragraph.BulletFontSizePt ?? _fallbackFontSizePt),
            foreground,
            TextAlignment.Left,
            TextWrapping.NoWrap,
            TextTrimming.None,
            textDecorations: null,
            FlowDirection.LeftToRight,
            double.PositiveInfinity,
            double.PositiveInfinity,
            lineHeight: double.NaN,
            letterSpacing: 0,
            maxLines: 0,
            fontFeatures: null,
            textStyleOverrides: null);
    }

    private static Bitmap? CreateBulletImage(InCanvasRichTextVisualParagraph paragraph)
    {
        if (paragraph.BulletKind != BulletKind.Image
            || paragraph.BulletImage is not { Bytes.Length: > 0 } image)
            return null;

        try
        {
            using var stream = new MemoryStream(image.Bytes, writable: false);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private GenericTextRunProperties CreateRunProperties(InCanvasRichTextVisualRun? run)
    {
        var decorations = new TextDecorationCollection();
        if (run?.Underline == true)
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        if (run?.Strikethrough == true)
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });

        IBrush foreground = run?.Color is { } color
            ? new SolidColorBrush(Color.FromRgb(color.Resolved.R, color.Resolved.G, color.Resolved.B))
            : DefaultForeground;
        return new GenericTextRunProperties(
            CreateTypeface(run),
            ToDip(run?.FontSizePt ?? _fallbackFontSizePt),
            decorations.Count > 0 ? decorations : null,
            foreground,
            baselineAlignment: run?.BaselineOffset switch
            {
                > 0 => BaselineAlignment.Superscript,
                < 0 => BaselineAlignment.Subscript,
                _ => BaselineAlignment.Baseline,
            });
    }

    private Typeface CreateTypeface(InCanvasRichTextVisualRun? run)
    {
        var family = new FontFamily(
            string.IsNullOrWhiteSpace(run?.FontFamily) ? _fallbackFontFamily : run.FontFamily);
        return new Typeface(
            family,
            run?.Italic == true ? FontStyle.Italic : FontStyle.Normal,
            run?.Bold == true ? FontWeight.Bold : FontWeight.Normal);
    }

    private IReadOnlyList<Rect> BuildSelectionRects()
    {
        int selectionStart = Math.Min(_selectionStart, _selectionEnd);
        int selectionEnd = Math.Max(_selectionStart, _selectionEnd);
        if (selectionStart == selectionEnd)
            return [];

        var result = new List<Rect>();
        foreach (var item in _layouts)
        {
            int overlapStart = Math.Max(selectionStart, item.Paragraph.GlobalStart);
            int overlapEnd = Math.Min(selectionEnd, item.Paragraph.GlobalEnd);
            if (overlapEnd <= overlapStart)
                continue;

            int displayStart = overlapStart - item.Paragraph.GlobalStart;
            foreach (var rect in item.Layout.HitTestTextRange(displayStart, overlapEnd - overlapStart))
                result.Add(rect.Translate(item.Origin));
        }

        return result;
    }

    private Rect BuildCaretRect()
    {
        if (!_showCaret || _selectionStart != _selectionEnd || _layouts.Count == 0)
            return default;

        int logicalPosition = Math.Clamp(_selectionEnd, 0, _plan.PlainText.Length);
        var item = _layouts[FindParagraphIndex(logicalPosition)];
        int displayPosition = ToDisplayPosition(item.Paragraph, logicalPosition);
        var hit = item.Layout.HitTestTextPosition(displayPosition);
        return new Rect(
            item.Origin.X + hit.X,
            item.Origin.Y + hit.Y,
            CaretWidth,
            Math.Max(1, hit.Height));
    }

    private int FindParagraphIndex(int logicalPosition)
    {
        int clamped = Math.Clamp(logicalPosition, 0, _plan.PlainText.Length);
        for (int index = _layouts.Count - 1; index >= 0; index--)
        {
            if (clamped >= _layouts[index].Paragraph.GlobalStart)
                return index;
        }
        return 0;
    }

    private static int ToDisplayPosition(
        InCanvasRichTextVisualParagraph paragraph,
        int logicalPosition) =>
        Math.Clamp(logicalPosition - paragraph.GlobalStart, 0, paragraph.Text.Length);

    private static TextAlignment ToAvaloniaAlignment(TextAlign alignment) => alignment switch
    {
        TextAlign.Center => TextAlignment.Center,
        TextAlign.Right => TextAlignment.Right,
        TextAlign.Justify or TextAlign.Distributed => TextAlignment.Justify,
        _ => TextAlignment.Left,
    };

    private static double ToDip(double pointSize) => Math.Max(1, pointSize * PtToDip);

    private static bool AreClose(double left, double right) =>
        double.IsFinite(left)
        && double.IsFinite(right)
        && Math.Abs(left - right) < 0.01;

    private void InvalidateLayouts()
    {
        DisposeLayouts();
        _layoutWidth = double.NaN;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void DisposeLayouts()
    {
        foreach (var item in _layouts)
        {
            item.Layout.Dispose();
            item.BulletLayout?.Dispose();
            item.BulletImage?.Dispose();
        }
        _layouts.Clear();
    }

    private sealed record ParagraphLayout(
        InCanvasRichTextVisualParagraph Paragraph,
        TextLayout Layout,
        Point Origin,
        TextLayout? BulletLayout,
        Point BulletOrigin,
        Bitmap? BulletImage)
    {
        internal double Bottom => Origin.Y + Layout.Height + Paragraph.SpaceAfterDip;
    }
}
