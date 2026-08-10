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
    private const double NativeEditorRightInset = 3;
    // WPF's RichTextBox places the first glyph at the editor's top content edge.
    // Keep the horizontal inset, but do not add a vertical offset to the custom surface.
    private static readonly Thickness ContentPadding = new(4, 0, 4, 3);
    private static readonly IBrush DefaultForeground = Brushes.Black;
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromArgb(
        InCanvasRichTextSelectionVisualContract.RealizedBackgroundAlpha,
        InCanvasRichTextSelectionVisualContract.RealizedBackgroundRed,
        InCanvasRichTextSelectionVisualContract.RealizedBackgroundGreen,
        InCanvasRichTextSelectionVisualContract.RealizedBackgroundBlue));
    private static readonly IBrush SelectionForeground = new SolidColorBrush(Color.FromArgb(
        InCanvasRichTextSelectionVisualContract.RealizedForegroundAlpha,
        InCanvasRichTextSelectionVisualContract.RealizedForegroundRed,
        InCanvasRichTextSelectionVisualContract.RealizedForegroundGreen,
        InCanvasRichTextSelectionVisualContract.RealizedForegroundBlue));
    private static readonly IPen CaretPen = new Pen(Brushes.Black, CaretWidth);

    private readonly List<ParagraphLayout> _layouts = [];
    private InCanvasRichTextVisualPlan _plan = InCanvasRichTextVisualPlanner.Create(null);
    private string _fallbackFontFamily = InCanvasRichTextEditorDefaults.FallbackFontFamily;
    private double _fallbackFontSizePt = InCanvasRichTextEditorDefaults.ShapeFallbackFontSizePt;
    private double _layoutWidth = double.NaN;
    private double _scrollOffsetX;
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

    internal double ScrollOffsetX => _scrollOffsetX;

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
        EnsureLayouts();
        RevealSelectionHorizontally();
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
            Math.Max(0, point.X + _scrollOffsetX - item.Origin.X),
            Math.Clamp(documentY - item.Origin.Y, 0, item.Layout.Height));
        var hit = item.Layout.HitTestPoint(localPoint);
        int localTextPosition = Math.Clamp(
            hit.TextPosition,
            0,
            item.Paragraph.Text.Length);
        return item.Paragraph.GlobalStart + localTextPosition;
    }

    internal bool TryHitTestInlineTableCell(
        Point point,
        out InlineTableCellHit hit)
    {
        EnsureLayouts();
        var documentPoint = new Point(
            point.X + _scrollOffsetX,
            point.Y + _scrollOffsetY);

        foreach (var paragraph in _layouts)
        {
            foreach (var table in paragraph.InlineTables)
            {
                var inlineRect = paragraph.Layout.HitTestTextPosition(table.Start);
                var tableOrigin = new Point(
                    paragraph.Origin.X + inlineRect.X,
                    paragraph.Origin.Y + inlineRect.Y);
                if (!new Rect(tableOrigin, new Size(table.WidthDip, table.HeightDip))
                        .Contains(documentPoint))
                    continue;

                if (InlineTableTextRun.TryGetCellAt(
                        table,
                        tableOrigin,
                        documentPoint,
                        out var rowIndex,
                        out var columnIndex,
                        out var cellBounds)
                    && InlineTableTextRun.TryGetCell(
                        table,
                        tableOrigin,
                        rowIndex,
                        columnIndex,
                        out var cell)
                    && cell.Cell?.TextBody is not null)
                {
                    hit = new InlineTableCellHit(
                        paragraph.Paragraph.GlobalStart + table.Start,
                        table.Info,
                        rowIndex,
                        columnIndex,
                        cellBounds,
                        cell.SourceCellIndex);
                    return true;
                }
            }
        }

        hit = default;
        return false;
    }

    internal IReadOnlyList<AvaloniaInlineOleHostRequest> GetInlineOleHits()
    {
        EnsureLayouts();
        var hits = new List<AvaloniaInlineOleHostRequest>();
        foreach (var paragraph in _layouts)
        {
            foreach (var ole in paragraph.InlineOleObjects)
            {
                var run = paragraph.Paragraph.Runs.FirstOrDefault(candidate =>
                    candidate.Start == ole.Start
                    && candidate.InlineOleObject is not null);
                if (run?.InlineOleObject is not { } inlineObject)
                    continue;

                var inlineRect = paragraph.Layout.HitTestTextPosition(ole.Start);
                hits.Add(new AvaloniaInlineOleHostRequest(
                    paragraph.Paragraph.GlobalStart + ole.Start,
                    inlineObject,
                    new Rect(
                        paragraph.Origin.X + inlineRect.X - _scrollOffsetX,
                        paragraph.Origin.Y + inlineRect.Y - _scrollOffsetY,
                        ole.WidthDip,
                        ole.HeightDip)));
            }
        }

        return hits;
    }

    internal bool TryFindInlineTableCell(
        InlineTableCellHit source,
        int rowIndex,
        int columnIndex,
        out InlineTableCellHit hit)
    {
        EnsureLayouts();
        foreach (var paragraph in _layouts)
        {
            foreach (var table in paragraph.InlineTables)
            {
                if (!MatchesInlineTable(paragraph, table, source))
                    continue;

                var inlineRect = paragraph.Layout.HitTestTextPosition(table.Start);
                var tableOrigin = new Point(
                    paragraph.Origin.X + inlineRect.X,
                    paragraph.Origin.Y + inlineRect.Y);
                if (!InlineTableTextRun.TryGetCell(
                        table,
                        tableOrigin,
                        rowIndex,
                        columnIndex,
                        out var cell)
                    || cell.Cell?.TextBody is null)
                {
                    continue;
                }

                hit = new InlineTableCellHit(
                    source.LogicalPosition,
                    table.Info,
                    cell.RowIndex,
                    cell.ColumnIndex,
                    cell.Bounds,
                    cell.SourceCellIndex);
                return true;
            }
        }

        hit = default;
        return false;
    }

    internal bool TryFindAdjacentInlineTableCell(
        InlineTableCellHit source,
        bool backwards,
        out InlineTableCellHit hit)
    {
        EnsureLayouts();
        foreach (var paragraph in _layouts)
        {
            foreach (var table in paragraph.InlineTables)
            {
                if (!MatchesInlineTable(paragraph, table, source))
                    continue;

                var current = table.Plan.ResolveCell(
                    source.RowIndex,
                    source.ColumnIndex);
                if (current is null
                    || !table.Plan.TryGetAdjacent(current, backwards, out var target))
                    continue;

                var tableOrigin = new Point(
                    paragraph.Origin.X + paragraph.Layout.HitTestTextPosition(table.Start).X,
                    paragraph.Origin.Y + paragraph.Layout.HitTestTextPosition(table.Start).Y);
                var grid = AvaloniaInlineTableGridLayout.Create(
                    table.Plan,
                    tableOrigin);
                if (grid.GetCell(target.RowIndex, target.ColumnIndex) is { } cell
                    && cell.Cell?.TextBody is not null)
                {
                    hit = new InlineTableCellHit(
                        source.LogicalPosition,
                        table.Info,
                        cell.RowIndex,
                        cell.ColumnIndex,
                        cell.Bounds,
                        cell.SourceCellIndex);
                    return true;
                }
            }
        }

        hit = default;
        return false;
    }

    private static bool MatchesInlineTable(
        ParagraphLayout paragraph,
        InlineTableLayout table,
        InlineTableCellHit source)
    {
        if (ReferenceEquals(table.Info, source.Table))
            return true;

        return paragraph.Paragraph.GlobalStart + table.Start == source.LogicalPosition;
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
        // Selection can be assigned before this control receives its final arrange width.
        // Re-run the native-editor equivalent horizontal reveal once the viewport is real.
        RevealSelectionHorizontally();

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
                        item.BulletOrigin.X - _scrollOffsetX,
                        item.BulletOrigin.Y - _scrollOffsetY,
                        size,
                        size));
            }

            item.Layout.Draw(
                context,
                new Point(item.Origin.X - _scrollOffsetX, item.Origin.Y - _scrollOffsetY));

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
            SelectionForeground,
            item.InlineImages,
            item.InlineOleObjects);
        foreach (var rect in selectedLayout.HitTestTextRange(
                     start - item.Paragraph.GlobalStart,
                     end - start))
        {
            var translated = rect.Translate(item.Origin);
            var screenRect = new Rect(
                translated.X - _scrollOffsetX,
                translated.Y - _scrollOffsetY,
                translated.Width,
                translated.Height);
            if (!selectionRects.Any(selection => selection.Intersects(screenRect)))
                continue;

            using (context.PushClip(screenRect))
                selectedLayout.Draw(
                    context,
                    new Point(item.Origin.X - _scrollOffsetX, item.Origin.Y - _scrollOffsetY));
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
            var inlineImages = CreateInlineImages(paragraph);
            var inlineOleObjects = CreateInlineOleObjects(paragraph);
            var inlineTables = CreateInlineTables(paragraph, maxWidth);
            var layout = CreateLayout(
                paragraph,
                maxWidth,
                inlineImages: inlineImages,
                inlineOleObjects: inlineOleObjects,
                inlineTables: inlineTables);
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
                inlineImages,
                inlineOleObjects,
                inlineTables,
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
        IBrush? foregroundOverride = null,
        IReadOnlyList<InlineImageLayout>? inlineImages = null,
        IReadOnlyList<InlineOleLayout>? inlineOleObjects = null,
        IReadOnlyList<InlineTableLayout>? inlineTables = null,
        bool? wrapOverride = null)
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

        if (inlineImages is { Count: > 0 }
            || inlineOleObjects is { Count: > 0 }
            || inlineTables is { Count: > 0 })
        {
            var sourceRuns = paragraph.Runs
                .Where(run => run.Length > 0)
                .Select(run => new InlineTextSourceRun(
                    run.Start,
                    run.Length,
                    CreateRunProperties(run, foregroundOverride),
                    inlineImages?.FirstOrDefault(image => image.Start == run.Start),
                    inlineOleObjects?.FirstOrDefault(ole => ole.Start == run.Start),
                    inlineTables?.FirstOrDefault(table => table.Start == run.Start)))
                .ToArray();
            var source = new InlineImageTextSource(
                this,
                paragraph.Text,
                sourceRuns);
            var paragraphProperties = new GenericTextParagraphProperties(
                paragraph.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                ToAvaloniaAlignment(paragraph.Alignment),
                firstLineInParagraph: true,
                alwaysCollapsible: false,
                new GenericTextRunProperties(
                    defaultTypeface,
                    defaultFontSize,
                    null,
                    foregroundOverride ?? DefaultForeground),
                (wrapOverride ?? _plan.Wrap) ? TextWrapping.Wrap : TextWrapping.NoWrap,
                lineHeight: 0,
                indent: 0,
                letterSpacing: 0);
            return new TextLayout(
                source,
                paragraphProperties,
                TextTrimming.None,
                maxWidth,
                double.PositiveInfinity,
                maxLines: 0);
        }

        return new TextLayout(
            paragraph.Text,
            defaultTypeface,
            defaultFontSize,
            foregroundOverride ?? DefaultForeground,
            ToAvaloniaAlignment(paragraph.Alignment),
            (wrapOverride ?? _plan.Wrap) ? TextWrapping.Wrap : TextWrapping.NoWrap,
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

        IBrush foreground = foregroundOverride ?? (run?.InlineImage is not null
            || run?.InlineOleObject is not null
            || run?.InlineTable is not null
            ? Brushes.Transparent
            : run?.Color is { } color
            ? new SolidColorBrush(Color.FromRgb(color.Resolved.R, color.Resolved.G, color.Resolved.B))
            : DefaultForeground);
        double fontSize = run?.InlineImage is not null
            ? InlineImageHeightDip(run)
            : run?.InlineOleObject is not null
                ? InlineOleHeightDip(run)
            : run?.InlineTable is not null
                ? InlineTableHeightDip(run)
            : ToDip(run?.FontSizePt ?? _fallbackFontSizePt);
        return new GenericTextRunProperties(
            CreateTypeface(run),
            fontSize,
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

    private IReadOnlyList<InlineImageLayout> CreateInlineImages(
        InCanvasRichTextVisualParagraph paragraph)
    {
        var result = new List<InlineImageLayout>();
        foreach (var run in paragraph.Runs.Where(run => run.InlineImage is { Bytes.Length: > 0 }))
        {
            try
            {
                using var stream = new MemoryStream(run.InlineImage!.Bytes, writable: false);
                var bitmap = new Bitmap(stream);
                result.Add(new InlineImageLayout(
                    run.Start,
                    bitmap,
                    InlineImageWidthDip(run),
                    InlineImageHeightDip(run)));
            }
            catch
            {
                // Malformed clipboard media remains a one-character text run.
            }
        }

        return result;
    }

    private static IReadOnlyList<InlineOleLayout> CreateInlineOleObjects(
        InCanvasRichTextVisualParagraph paragraph)
    {
        return paragraph.Runs
            .Where(run => run.InlineOleObject is { EmbeddedBytes.Length: > 0 })
            .Select(run => new InlineOleLayout(
                run.Start,
                42,
                Math.Max(18, run.FontSizePt is > 0
                    ? run.FontSizePt.Value * PtToDip + 4
                    : 20)))
            .ToArray();
    }

    private static IReadOnlyList<InlineTableLayout> CreateInlineTables(
        InCanvasRichTextVisualParagraph paragraph,
        double availableWidthDip)
    {
        var result = new List<InlineTableLayout>();
        foreach (var run in paragraph.Runs.Where(run => run.InlineTable is not null))
        {
            var layout = InlineTableLogicalGridPlan.CreateLayout(
                run.InlineTable!.Table,
                availableWidthDip);
            result.Add(new InlineTableLayout(
                run.Start,
                run.InlineTable,
                layout.WidthDip,
                layout.HeightDip,
                layout));
        }
        return result;
    }

    private static double InlineImageWidthDip(InCanvasRichTextVisualRun run)
    {
        if (run.InlineImageWidthEmu is > 0)
            return Math.Max(1, run.InlineImageWidthEmu.Value / 9525.0);
        if (run.InlineImageHeightEmu is > 0)
            return Math.Max(1, run.InlineImageHeightEmu.Value / 9525.0);
        return InlineImageHeightDip(run);
    }

    private static double InlineImageHeightDip(InCanvasRichTextVisualRun run) =>
        run.InlineImageHeightEmu is > 0
            ? Math.Max(1, run.InlineImageHeightEmu.Value / 9525.0)
            : Math.Max(1, run.FontSizePt is > 0 ? run.FontSizePt.Value * PtToDip : 16);

    private static double InlineOleHeightDip(InCanvasRichTextVisualRun run) =>
        Math.Max(18, run.FontSizePt is > 0 ? run.FontSizePt.Value * PtToDip + 4 : 20);

    private static double InlineTableHeightDip(InCanvasRichTextVisualRun run)
    {
        if (run.InlineTable is not { } table)
            return 24;
        return InlineTableLogicalGridPlan.CreateLayout(table.Table).HeightDip;
    }

    private IReadOnlyList<Rect> BuildSelectionRects(bool includeHorizontalScroll = true)
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
                    translated.X - (includeHorizontalScroll ? _scrollOffsetX : 0),
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
            item.Origin.X + hit.X - _scrollOffsetX,
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
                            item.Origin.X + hit.X - _scrollOffsetX);
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
        _scrollOffsetX = 0;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void RevealSelectionHorizontally()
    {
        var selectionRects = BuildSelectionRects(includeHorizontalScroll: false);
        if (selectionRects.Count == 0)
            return;

        double viewportLeft = ContentPadding.Left;
        // WPF's RichTextBox keeps its selection inside the native border/padding inset.
        // The transparent Avalonia input has no painted border, so retain that inset when
        // revealing the custom surface to keep the two editor viewports aligned.
        double viewportRight = Math.Max(
            viewportLeft,
            Bounds.Width - ContentPadding.Right - NativeEditorRightInset);
        double left = selectionRects.Min(rect => rect.Left);
        double right = selectionRects.Max(rect => rect.Right);
        double offset = _scrollOffsetX;
        if (right - offset > viewportRight)
            offset = right - viewportRight;
        if (left - offset < viewportLeft)
            offset = left - viewportLeft;
        _scrollOffsetX = Math.Max(0, offset);
    }

    private void DisposeLayouts()
    {
        foreach (var item in _layouts)
        {
            item.Layout.Dispose();
            item.BulletLayout?.Dispose();
            item.BulletImage?.Dispose();
            foreach (var image in item.InlineImages)
                image.Bitmap.Dispose();
        }
        _layouts.Clear();
    }

    private sealed class InlineImageTextSource : ITextSource
    {
        private readonly AvaloniaRichTextEditingSurface _owner;
        private readonly string _text;
        private readonly IReadOnlyList<InlineTextSourceRun> _runs;

        internal InlineImageTextSource(
            AvaloniaRichTextEditingSurface owner,
            string text,
            IReadOnlyList<InlineTextSourceRun> runs)
        {
            _owner = owner;
            _text = text;
            _runs = runs;
        }

        public TextRun? GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex < 0 || textSourceIndex >= _text.Length)
                return null;

            var run = _runs.FirstOrDefault(candidate =>
                textSourceIndex >= candidate.Start
                && textSourceIndex < candidate.Start + candidate.Length);
            if (run is null)
                return new TextCharacters(_text.AsMemory(textSourceIndex),
                    new GenericTextRunProperties(
                        new Typeface(new FontFamily(InCanvasRichTextEditorDefaults.FallbackFontFamily)),
                        12,
                        null,
                        Brushes.Black));

            int offset = textSourceIndex - run.Start;
            if (run.InlineImage is { } image && offset == 0)
            {
                return new InlineImageTextRun(
                    image.Bitmap,
                    new Size(image.WidthDip, image.HeightDip),
                    run.Properties);
            }

            if (run.InlineOleObject is { } ole && offset == 0)
            {
                return new InlineOleTextRun(
                    new Size(ole.WidthDip, ole.HeightDip),
                    run.Properties);
            }

            if (run.InlineTable is { } table && offset == 0)
            {
                return new InlineTableTextRun(
                    _owner,
                    table,
                    new Size(table.WidthDip, table.HeightDip),
                    run.Properties);
            }

            int length = Math.Min(run.Length - offset, _text.Length - textSourceIndex);
            return new TextCharacters(
                _text.AsMemory(textSourceIndex, length),
                run.Properties);
        }
    }

    private sealed class InlineImageTextRun : DrawableTextRun
    {
        private readonly Bitmap _bitmap;
        private readonly Size _size;
        private readonly TextRunProperties _properties;

        internal InlineImageTextRun(
            Bitmap bitmap,
            Size size,
            TextRunProperties properties)
        {
            _bitmap = bitmap;
            _size = size;
            _properties = properties;
        }

        public override int Length => 1;

        public override TextRunProperties Properties => _properties;

        public override Size Size => _size;

        public override double Baseline => _size.Height;

        public override void Draw(DrawingContext drawingContext, Point origin)
        {
            drawingContext.DrawImage(
                _bitmap,
                new Rect(origin.X, origin.Y, _size.Width, _size.Height));
        }
    }

    private sealed class InlineOleTextRun : DrawableTextRun
    {
        private readonly Size _size;
        private readonly TextRunProperties _properties;

        internal InlineOleTextRun(Size size, TextRunProperties properties)
        {
            _size = size;
            _properties = properties;
        }

        public override int Length => 1;

        public override TextRunProperties Properties => _properties;

        public override Size Size => _size;

        public override double Baseline => _size.Height;

        public override void Draw(DrawingContext drawingContext, Point origin)
        {
            var rect = new Rect(origin.X, origin.Y, _size.Width, _size.Height);
            drawingContext.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(232, 232, 232)),
                new Pen(Brushes.Gray, 1),
                rect);
        }
    }

    private sealed class InlineTableTextRun : DrawableTextRun
    {
        private readonly AvaloniaRichTextEditingSurface _owner;
        private readonly InlineTableLayout _table;
        private readonly Size _size;
        private readonly TextRunProperties _properties;

        internal InlineTableTextRun(
            AvaloniaRichTextEditingSurface owner,
            InlineTableLayout table,
            Size size,
            TextRunProperties properties)
        {
            _owner = owner;
            _table = table;
            _size = size;
            _properties = properties;
        }

        public override int Length => 1;
        public override TextRunProperties Properties => _properties;
        public override Size Size => _size;
        public override double Baseline => _size.Height;

        public override void Draw(DrawingContext drawingContext, Point origin)
        {
            var grid = AvaloniaInlineTableGridLayout.Create(
                _table.Plan,
                origin);
            foreach (var cellLayout in grid.Cells)
            {
                var cell = cellLayout.Cell;
                IBrush fill = cell?.Fill is ShapeFill.Solid solid
                    ? (IBrush)new SolidColorBrush(Color.FromRgb(
                        solid.Color.Resolved.R,
                        solid.Color.Resolved.G,
                        solid.Color.Resolved.B))
                    : Brushes.Transparent;
                drawingContext.DrawRectangle(
                    fill,
                    new Pen(Brushes.Gray, 0.5),
                    cellLayout.Bounds);
                if (cell?.TextBody is { } body)
                    DrawCellBody(drawingContext, cell, cellLayout.Bounds, body);
            }
        }

        internal static bool TryGetCellAt(
            InlineTableLayout tableLayout,
            Point origin,
            Point point,
            out int rowIndex,
            out int columnIndex,
            out Rect cellBounds)
        {
            var grid = AvaloniaInlineTableGridLayout.Create(
                tableLayout.Plan,
                origin);
            if (grid.HitTest(point) is { } hit)
            {
                rowIndex = hit.RowIndex;
                columnIndex = hit.ColumnIndex;
                cellBounds = hit.Bounds;
                return true;
            }

            rowIndex = -1;
            columnIndex = -1;
            cellBounds = default;
            return false;
        }

        internal static bool TryGetCellBounds(
            InlineTableLayout tableLayout,
            Point origin,
            int targetRow,
            int targetColumn,
            out Rect cellBounds)
        {
            var grid = AvaloniaInlineTableGridLayout.Create(
                tableLayout.Plan,
                origin);
            if (grid.GetCell(targetRow, targetColumn) is { } cell)
            {
                cellBounds = cell.Bounds;
                return true;
            }

            cellBounds = default;
            return false;
        }

        internal static bool TryGetCell(
            InlineTableLayout tableLayout,
            Point origin,
            int targetRow,
            int targetColumn,
            out AvaloniaInlineTableCellLayout cell)
        {
            var grid = AvaloniaInlineTableGridLayout.Create(
                tableLayout.Plan,
                origin);
            if (grid.GetCell(targetRow, targetColumn) is { } layout)
            {
                cell = layout;
                return true;
            }

            cell = null!;
            return false;
        }

        private void DrawCellBody(
            DrawingContext drawingContext,
            TableCell cell,
            Rect cellBounds,
            TextBody body)
        {
            var textArea = AvaloniaInlineTableLayoutPlanner.GetTextArea(cell, cellBounds);
            var plan = InCanvasRichTextVisualPlanner.Create(body);
            bool rotatedText = AvaloniaInlineTableLayoutPlanner.IsRotatedText(body.VerticalType);
            double layoutWidth = rotatedText
                ? Math.Max(1, textArea.Height)
                : Math.Max(1, textArea.Width);
            var layouts = new List<(
                InCanvasRichTextVisualParagraph Paragraph,
                TextLayout Layout,
                IReadOnlyList<InlineImageLayout> InlineImages)>();
            double contentHeight = 0;

            foreach (var paragraph in plan.Paragraphs)
            {
                var inlineImages = _owner.CreateInlineImages(paragraph);
                var layout = _owner.CreateLayout(
                    paragraph,
                    layoutWidth,
                    inlineImages: inlineImages,
                    inlineOleObjects: CreateInlineOleObjects(paragraph),
                    inlineTables: CreateInlineTables(paragraph, textArea.Width),
                    wrapOverride: plan.Wrap);
                layouts.Add((paragraph, layout, inlineImages));
                contentHeight += paragraph.SpaceBeforeDip
                    + (rotatedText ? GetTextLayoutWidth(layout) : layout.Height)
                    + paragraph.SpaceAfterDip;
            }

            var contentOrigin = AvaloniaInlineTableLayoutPlanner.GetTextOrigin(
                cell,
                textArea,
                contentHeight);
            double y = contentOrigin.Y;

            using (drawingContext.PushClip(textArea))
            {
                foreach (var (paragraph, layout, _) in layouts)
                {
                    y += paragraph.SpaceBeforeDip;
                    if (y >= textArea.Bottom)
                        break;

                    if (rotatedText)
                    {
                        var rotated = AvaloniaInlineTableLayoutPlanner.PlanRotatedText(
                            body.VerticalType,
                            textArea,
                            new Size(GetTextLayoutWidth(layout), layout.Height),
                            y);
                        using var transformScope = drawingContext.PushTransform(rotated.Transform);
                        layout.Draw(drawingContext, rotated.Origin);
                        y += GetTextLayoutWidth(layout) + paragraph.SpaceAfterDip;
                    }
                    else
                    {
                        layout.Draw(drawingContext, new Point(textArea.X, y));
                        y += layout.Height + paragraph.SpaceAfterDip;
                    }
                }
            }

            foreach (var (_, _, inlineImages) in layouts)
            {
                foreach (var image in inlineImages)
                    image.Bitmap.Dispose();
            }
        }

        private static double GetTextLayoutWidth(TextLayout layout) =>
            layout.TextLines.Count == 0
                ? 0
                : layout.TextLines.Max(line => line.WidthIncludingTrailingWhitespace);
    }

    private sealed record InlineTextSourceRun(
        int Start,
        int Length,
        TextRunProperties Properties,
        InlineImageLayout? InlineImage,
        InlineOleLayout? InlineOleObject,
        InlineTableLayout? InlineTable);

    private sealed record ParagraphLayout(
        InCanvasRichTextVisualParagraph Paragraph,
        TextLayout Layout,
        Point Origin,
        TextLayout? BulletLayout,
        Point BulletOrigin,
        Bitmap? BulletImage,
        IReadOnlyList<InlineImageLayout> InlineImages,
        IReadOnlyList<InlineOleLayout> InlineOleObjects,
        IReadOnlyList<InlineTableLayout> InlineTables,
        FlowDirection FlowDirection)
    {
        internal double Bottom => Origin.Y + Layout.Height + Paragraph.SpaceAfterDip;
    }

    private sealed record InlineImageLayout(
        int Start,
        Bitmap Bitmap,
        double WidthDip,
        double HeightDip);

    private sealed record InlineOleLayout(
        int Start,
        double WidthDip,
        double HeightDip);
    private sealed record InlineTableLayout(
        int Start,
        InlineTableInfo Info,
        double WidthDip,
        double HeightDip,
        InlineTableLayoutPlan Plan);

    internal readonly record struct InlineTableCellHit(
        int LogicalPosition,
        InlineTableInfo Table,
        int RowIndex,
        int ColumnIndex,
        Rect Bounds,
        int SourceCellIndex);
}

/// <summary>
/// Measured inline OLE placement supplied to a host-specific in-place editor.
/// The request is deliberately renderer-neutral so headless and non-Windows Avalonia
/// builds retain the replacement glyph path when no native host is available.
/// </summary>
public sealed record AvaloniaInlineOleHostRequest(
    int LogicalPosition,
    InlineOleObjectInfo InlineObject,
    Rect Bounds);
