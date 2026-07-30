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
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromArgb(
        InCanvasRichTextSelectionVisualContract.BackgroundAlpha,
        InCanvasRichTextSelectionVisualContract.BackgroundRed,
        InCanvasRichTextSelectionVisualContract.BackgroundGreen,
        InCanvasRichTextSelectionVisualContract.BackgroundBlue));
    private static readonly IBrush SelectionForeground = new SolidColorBrush(Color.FromArgb(
        InCanvasRichTextSelectionVisualContract.ForegroundAlpha,
        InCanvasRichTextSelectionVisualContract.ForegroundRed,
        InCanvasRichTextSelectionVisualContract.ForegroundGreen,
        InCanvasRichTextSelectionVisualContract.ForegroundBlue));
    private static readonly IPen CaretPen = new Pen(Brushes.Black, CaretWidth);

    private readonly List<ParagraphLayout> _layouts = [];
    private InCanvasRichTextVisualPlan _plan = InCanvasRichTextVisualPlanner.Create(null);
    private string _fallbackFontFamily = InCanvasRichTextEditorDefaults.FallbackFontFamily;
    private double _fallbackFontSizePt = InCanvasRichTextEditorDefaults.ShapeFallbackFontSizePt;
    private double _layoutWidth = double.NaN;
    private double _scrollOffsetY;
    private double _contentExtentHeight;
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

    internal double ScrollOffsetY => _scrollOffsetY;

    internal double ContentExtentHeight
    {
        get
        {
            EnsureLayouts();
            return _contentExtentHeight;
        }
    }

    internal IReadOnlyList<Rect> SelectionRects
    {
        get
        {
            EnsureLayouts();
            return BuildSelectionRects();
        }
    }

    internal IReadOnlyList<FlowDirection> LayoutFlowDirections
    {
        get
        {
            EnsureLayouts();
            return _layouts.Select(item => item.FlowDirection).ToArray();
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

        double documentY = point.Y + _scrollOffsetY;
        var item = _layouts
            .OrderBy(candidate => VerticalDistance(documentY, candidate.Origin.Y, candidate.Bottom))
            .First();
        var localPoint = new Point(
            Math.Max(0, point.X - item.Origin.X),
            Math.Clamp(documentY - item.Origin.Y, 0, item.Layout.Height));
        var hit = item.Layout.HitTestPoint(localPoint);
        int localTextPosition = Math.Clamp(
            hit.TextPosition,
            0,
            item.Paragraph.Text.Length);
        return item.Paragraph.GlobalStart + localTextPosition;
    }

    internal void SetScrollOffset(double offsetY)
    {
        EnsureLayouts();
        double maximum = Math.Max(0, _contentExtentHeight - Bounds.Height);
        double clamped = Math.Clamp(offsetY, 0, maximum);
        if (Math.Abs(_scrollOffsetY - clamped) < 0.01)
            return;

        _scrollOffsetY = clamped;
        InvalidateVisual();
    }

    private static double VerticalDistance(double value, double top, double bottom)
    {
        if (value < top)
            return top - value;
        if (value > bottom)
            return value - bottom;
        return 0;
    }

    internal InCanvasTextVerticalNavigationResult MoveCaretVertically(
        int logicalPosition,
        int lineDelta,
        double? preferredX = null,
        int? currentVisualLineIndex = null)
    {
        EnsureLayouts();
        if (_layouts.Count == 0 || lineDelta == 0)
            return new(
                Math.Clamp(logicalPosition, 0, _plan.PlainText.Length),
                preferredX ?? 0,
                0,
                false);

        return InCanvasRichTextNavigationPlanner.MoveCaretVertically(
            BuildVisualLineGeometry(),
            Math.Clamp(logicalPosition, 0, _plan.PlainText.Length),
            lineDelta < 0
                ? InCanvasTextVerticalDirection.Up
                : InCanvasTextVerticalDirection.Down,
            preferredX,
            currentVisualLineIndex);
    }

    internal int MoveCaretToVisualLineBoundary(int logicalPosition, bool end)
    {
        EnsureLayouts();
        if (_layouts.Count == 0)
            return 0;

        return InCanvasRichTextNavigationPlanner.MoveCaretToVisualLineBoundary(
            BuildVisualLineGeometry(),
            Math.Clamp(logicalPosition, 0, _plan.PlainText.Length),
            end);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureLayouts();

        var selectionRects = BuildSelectionRects();
        foreach (var rect in selectionRects)
            context.FillRectangle(SelectionBrush, rect);

        foreach (var item in _layouts)
        {
            if (item.BulletLayout is not null)
                item.BulletLayout.Draw(
                    context,
                    new Point(item.BulletOrigin.X, item.BulletOrigin.Y - _scrollOffsetY));
            else if (item.BulletImage is not null)
            {
                double size = Math.Max(1, ToDip(item.Paragraph.BulletFontSizePt ?? _fallbackFontSizePt));
                context.DrawImage(
                    item.BulletImage,
                    new Rect(
                        item.BulletOrigin.X,
                        item.BulletOrigin.Y - _scrollOffsetY,
                        size,
                        size));
            }

            item.Layout.Draw(
                context,
                new Point(item.Origin.X, item.Origin.Y - _scrollOffsetY));

            DrawSelectedText(context, item, selectionRects);
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

    private void DrawSelectedText(
        DrawingContext context,
        ParagraphLayout item,
        IReadOnlyList<Rect> selectionRects)
    {
        if (selectionRects.Count == 0)
            return;

        int start = Math.Max(_selectionStart, item.Paragraph.GlobalStart);
        int end = Math.Min(_selectionEnd, item.Paragraph.GlobalEnd);
        if (end <= start)
            return;

        using var selectedLayout = CreateLayout(
            item.Paragraph,
            Math.Max(1, Bounds.Width - item.Origin.X - ContentPadding.Right),
            SelectionForeground);
        foreach (var rect in selectedLayout.HitTestTextRange(
                     start - item.Paragraph.GlobalStart,
                     end - start))
        {
            var translated = rect.Translate(item.Origin);
            var screenRect = new Rect(
                translated.X,
                translated.Y - _scrollOffsetY,
                translated.Width,
                translated.Height);
            if (!selectionRects.Any(selection => selection.Intersects(screenRect)))
                continue;

            using (context.PushClip(screenRect))
                selectedLayout.Draw(
                    context,
                    new Point(item.Origin.X, item.Origin.Y - _scrollOffsetY));
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
                CreateBulletImage(paragraph),
                paragraph.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight));
            y += layout.Height + paragraph.SpaceAfterDip;
        }

        _contentExtentHeight = Math.Max(y + ContentPadding.Bottom, Bounds.Height);
        _scrollOffsetY = Math.Clamp(
            _scrollOffsetY,
            0,
            Math.Max(0, _contentExtentHeight - Bounds.Height));
    }

    private TextLayout CreateLayout(
        InCanvasRichTextVisualParagraph paragraph,
        double maxWidth,
        IBrush? foregroundOverride = null)
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
                CreateRunProperties(run, foregroundOverride)));
        }

        return new TextLayout(
            paragraph.Text,
            defaultTypeface,
            defaultFontSize,
            foregroundOverride ?? DefaultForeground,
            ToAvaloniaAlignment(paragraph.Alignment),
            TextWrapping.Wrap,
            TextTrimming.None,
            textDecorations: null,
            paragraph.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
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
            paragraph.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
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

    private GenericTextRunProperties CreateRunProperties(
        InCanvasRichTextVisualRun? run,
        IBrush? foregroundOverride = null)
    {
        var decorations = new TextDecorationCollection();
        if (run?.Underline == true)
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        if (run?.Strikethrough == true)
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });

        IBrush foreground = foregroundOverride ?? (run?.Color is { } color
            ? new SolidColorBrush(Color.FromRgb(color.Resolved.R, color.Resolved.G, color.Resolved.B))
            : DefaultForeground);
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
            {
                var translated = rect.Translate(item.Origin);
                result.Add(new Rect(
                    translated.X,
                    translated.Y - _scrollOffsetY,
                    translated.Width,
                    translated.Height));
            }
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
            item.Origin.Y + hit.Y - _scrollOffsetY,
            CaretWidth,
            Math.Max(1, hit.Height));
    }

    private IReadOnlyList<InCanvasTextVisualLineGeometry> BuildVisualLineGeometry()
    {
        var result = new List<InCanvasTextVisualLineGeometry>();
        foreach (var item in _layouts)
        {
            foreach (var line in item.Layout.TextLines)
            {
                int localStart = Math.Clamp(
                    line.FirstTextSourceIndex,
                    0,
                    item.Paragraph.Text.Length);
                int localEnd = Math.Clamp(
                    localStart + Math.Max(0, line.Length - line.NewLineLength),
                    localStart,
                    item.Paragraph.Text.Length);
                var carets = Enumerable.Range(localStart, localEnd - localStart + 1)
                    .Select(localPosition =>
                    {
                        var hit = item.Layout.HitTestTextPosition(localPosition);
                        return new InCanvasTextVisualCaret(
                            item.Paragraph.GlobalStart + localPosition,
                            item.Origin.X + hit.X);
                    })
                    .ToArray();
                result.Add(new InCanvasTextVisualLineGeometry(
                    item.Paragraph.GlobalStart + localStart,
                    item.Paragraph.GlobalStart + localEnd,
                    carets));
            }
        }

        return result;
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
        Bitmap? BulletImage,
        FlowDirection FlowDirection)
    {
        internal double Bottom => Origin.Y + Layout.Height + Paragraph.SpaceAfterDip;
    }
}
