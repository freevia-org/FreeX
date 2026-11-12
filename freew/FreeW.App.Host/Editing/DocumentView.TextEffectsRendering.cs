using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using WpfList = System.Windows.Documents.List;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun = System.Windows.Documents.Run;
using WpfTable = System.Windows.Documents.Table;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Word 2010+ text effects (w14 glow / shadow / reflection / outline / fill) on ordinary body text.
/// <para>
/// The glyph FILL is a plain brush, so it goes on the WPF run's Foreground. The effects that paint
/// OUTSIDE the glyphs -- outline, shadow, glow, reflection -- have no slot on a FlowDocument run. On
/// screen <see cref="TextEffectsAdorner"/> draws them from the live glyph positions; Print, Print Preview
/// and PDF/XPS export draw them with <see cref="TextEffectsPrintPaginator"/> from the laid-out page's glyph
/// runs. Both paint through the one <see cref="TextEffectsPainter"/>. Effects with no drawable form
/// (3-D scene/bevel) are not drawn and round-trip untouched through the run's model snapshot.
/// </para>
/// </summary>
public sealed partial class DocumentView
{
    // The Foreground brush BuildRun set from a run's text fill, so committing the run does not read the
    // fill colour back as an explicit w:color edit.
    private static readonly ConditionalWeakTable<WpfRun, Brush> TextFillBrushes = new();

    // Resolved plans keyed by the immutable effects record + theme palette, so repaints are cheap.
    private static readonly ConditionalWeakTable<RunTextEffects, Tuple<ThemeColorScheme, RunTextEffectVisualPlan?>> TextEffectPlans = new();

    private TextEffectsAdorner? _textEffectsAdorner;

    private static RunTextEffectVisualPlan? ResolveTextEffectPlan(RunTextEffects? effects, ThemeColorScheme scheme)
    {
        if (effects is null)
            return null;

        if (TextEffectPlans.TryGetValue(effects, out var cached) && ReferenceEquals(cached.Item1, scheme))
            return cached.Item2;

        var plan = RunTextEffectVisualPlanner.Build(effects, scheme);
        TextEffectPlans.AddOrUpdate(effects, Tuple.Create(scheme, plan));
        return plan;
    }

    /// <summary>
    /// Paints a run's w14 text fill. Its other effects are drawn by the overlay, which
    /// <see cref="SyncTextEffectsAdorner"/> attaches after the flow is built.
    /// </summary>
    private static void ApplyTextEffectPresentation(WpfRun wpf, RunFormatting formatting, TextDocument document)
    {
        if (formatting.TextEffects is not { } effects
            || ResolveTextEffectPlan(effects, document.Theme.ColorScheme) is not { } plan)
        {
            return;
        }

        if (plan.Fill is { } fill)
        {
            var brush = TextEffectsPainter.CreateFillBrush(fill);
            wpf.Foreground = brush;
            TextFillBrushes.AddOrUpdate(wpf, brush);
        }
    }

    /// <summary>
    /// The colour a committed run reports: the live Foreground, except when it is still the brush
    /// BuildRun derived from the run's text fill (then the model colour is unchanged).
    /// </summary>
    private static string? ReadObservedForegroundHex(WpfRun run, RunFormatting rendered)
    {
        var local = run.ReadLocalValue(TextElement.ForegroundProperty);
        if (local is Brush brush && TextFillBrushes.TryGetValue(run, out var fillBrush) && ReferenceEquals(brush, fillBrush))
            return rendered.ColorHex;

        return local is SolidColorBrush solid ? ToHex(solid.Color) : null;
    }

    /// <summary>Called after every flow rebuild: keep the overlay only while some run needs it.</summary>
    private void SyncTextEffectsAdorner()
    {
        var scheme = _model.Theme.ColorScheme;
        var needed = Document is { } document
            && TextEffectsAdorner.EnumerateRuns(document.Blocks).Any(run => OverlayPlan(run, scheme) is not null);
        if (needed)
        {
            EnsureTextEffectsAdorner();
            return;
        }

        if (_textEffectsAdorner is not null && AdornerLayer.GetAdornerLayer(this) is { } layer)
        {
            layer.Remove(_textEffectsAdorner);
            _textEffectsAdorner.Dispose();
            _textEffectsAdorner = null;
        }
    }

    // The plan of a run whose effects need the overlay, or null (no effects, fill only, or invisible run).
    private static RunTextEffectVisualPlan? OverlayPlan(WpfRun run, ThemeColorScheme scheme) =>
        run.Tag is RunMarkers { CharacterFormat.RenderedFormatting.TextEffects: { } effects }
        && run.FontSize >= 1
        && ResolveTextEffectPlan(effects, scheme) is { NeedsOverlay: true } plan
            ? plan
            : null;

    /// <summary>
    /// Print / Print Preview / PDF / XPS: the page content is a clone of <paramref name="source"/>'s blocks,
    /// which loses the run tags that carry the effects. Walking both flows in step, every clone of a run
    /// whose effects need drawing gets a foreground brush of its own (same paint), and the returned map
    /// ties that brush to the run's plan so <see cref="TextEffectsPrintPaginator"/> can find the run's glyph
    /// runs on the laid-out page. Null when nothing needs drawing or the two flows do not line up.
    /// </summary>
    internal static IReadOnlyDictionary<Brush, RunTextEffectVisualPlan>? TagTextEffectsForPrint(
        FlowDocument source,
        IEnumerable<System.Windows.Documents.Block> cloneBlocks,
        ThemeColorScheme scheme)
    {
        var sourceRuns = TextEffectsAdorner.EnumerateRuns(source.Blocks).ToList();
        if (!sourceRuns.Any(run => OverlayPlan(run, scheme) is not null))
            return null;

        var cloneRuns = TextEffectsAdorner.EnumerateRuns(cloneBlocks).ToList();
        if (cloneRuns.Count != sourceRuns.Count)
            return null;

        var plans = new Dictionary<Brush, RunTextEffectVisualPlan>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < sourceRuns.Count; index++)
        {
            if (OverlayPlan(sourceRuns[index], scheme) is not { } plan || cloneRuns[index].Text != sourceRuns[index].Text)
                continue;

            var brush = (cloneRuns[index].Foreground ?? Brushes.Black).CloneCurrentValue();
            brush.Freeze();
            cloneRuns[index].Foreground = brush;
            plans[brush] = plan;
        }

        return plans.Count == 0 ? null : plans;
    }

    private void EnsureTextEffectsAdorner()
    {
        if (_textEffectsAdorner is not null)
        {
            _textEffectsAdorner.RequestRefresh();
            return;
        }

        if (AdornerLayer.GetAdornerLayer(this) is not { } layer)
        {
            Loaded -= OnLoadedSyncTextEffects;
            Loaded += OnLoadedSyncTextEffects;
            return;
        }

        _textEffectsAdorner = new TextEffectsAdorner(this);
        layer.Add(_textEffectsAdorner);
        _textEffectsAdorner.RequestRefresh();
    }

    private void OnLoadedSyncTextEffects(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedSyncTextEffects;
        EnsureTextEffectsAdorner();
    }

    /// <summary>
    /// Draws outline, shadow, glow and reflection for the runs that carry them, above the text layer,
    /// through <see cref="TextEffectsPainter"/>. Each blurred effect is its own child visual so the blur
    /// applies to that layer only.
    /// <para>
    /// It repaints whenever the text can have moved. Scrolling, resizing, editing, zoom and page-chrome
    /// changes request a refresh directly; anything else that re-lays the text out (view mode, page
    /// setup, window changes) is caught on <see cref="UIElement.LayoutUpdated"/> -- but only when a cheap
    /// layout signature (view size, page geometry and the positions of the first and last effect runs)
    /// actually changed since the last paint, and never while a refresh is running. LayoutUpdated fires
    /// after every layout pass anywhere, including the ones a refresh's own character-rect queries and
    /// new layers cause; the signature check is what lets it go idle instead of looping.
    /// </para>
    /// </summary>
    internal sealed class TextEffectsAdorner : Adorner, IDisposable
    {
        // Bounds the per-paint work on a pathological document; far above a screenful of effect text.
        private const int MaxGlyphsPerRefresh = 4_000;

        private readonly DocumentView _view;
        private readonly VisualCollection _layers;
        private bool _refreshPending;
        private bool _refreshing;
        private LayoutSignature? _paintedSignature;
        private List<WpfRun> _effectRuns = [];

        public TextEffectsAdorner(DocumentView view) : base(view)
        {
            _view = view;
            _layers = new VisualCollection(this);
            IsHitTestVisible = false;
            _view.AddHandler(System.Windows.Controls.ScrollViewer.ScrollChangedEvent, ScrollChangedHandler);
            _view.SizeChanged += OnViewSizeChanged;
            _view.TextChanged += OnViewTextChanged;
            _view.ZoomChanged += OnViewZoomChanged;
            _view.LayoutChanged += OnViewLayoutChanged;
            _view.LayoutUpdated += OnViewLayoutUpdated;
        }

        internal int LayerCount => _layers.Count;

        /// <summary>How many times the overlay has repainted (tests use it to prove the refresh goes idle).</summary>
        internal int RefreshCount { get; private set; }

        private System.Windows.Controls.ScrollChangedEventHandler ScrollChangedHandler => _scrollChangedHandler ??= (_, _) => RequestRefresh();

        private System.Windows.Controls.ScrollChangedEventHandler? _scrollChangedHandler;

        public void Dispose()
        {
            _view.RemoveHandler(System.Windows.Controls.ScrollViewer.ScrollChangedEvent, ScrollChangedHandler);
            _view.SizeChanged -= OnViewSizeChanged;
            _view.TextChanged -= OnViewTextChanged;
            _view.ZoomChanged -= OnViewZoomChanged;
            _view.LayoutChanged -= OnViewLayoutChanged;
            _view.LayoutUpdated -= OnViewLayoutUpdated;
        }

        private void OnViewSizeChanged(object sender, SizeChangedEventArgs e) => RequestRefresh();

        private void OnViewTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RequestRefresh();

        private void OnViewZoomChanged(object? sender, double zoom) => RequestRefresh();

        private void OnViewLayoutChanged(object? sender, EventArgs e) => RequestRefresh();

        private void OnViewLayoutUpdated(object? sender, EventArgs e)
        {
            if (_refreshing || _refreshPending)
                return;
            if (!Equals(CurrentSignature(), _paintedSignature))
                RequestRefresh();
        }

        protected override int VisualChildrenCount => _layers.Count;

        protected override Visual GetVisualChild(int index) => _layers[index];

        public void RequestRefresh()
        {
            if (_refreshPending)
                return;

            _refreshPending = true;
            // Loaded priority runs after the layout/render pass, so character rects are current.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _refreshPending = false;
                Refresh();
            }));
        }

        internal void Refresh()
        {
            _refreshing = true;
            try
            {
                RefreshCount++;
                _layers.Clear();
                _effectRuns = [];
                if (_view.Document is not { } document)
                {
                    _paintedSignature = CurrentSignature();
                    return;
                }

                var scheme = _view.Model.Theme.ColorScheme;
                var bounds = new Rect(_view.RenderSize);
                var budget = MaxGlyphsPerRefresh;
                var pixelsPerDip = VisualTreeHelper.GetDpi(_view).PixelsPerDip;
                var blurred = new List<Visual>();
                var top = new DrawingVisual();
                using (var topContext = top.RenderOpen())
                {
                    foreach (var run in EnumerateRuns(document.Blocks))
                    {
                        if (OverlayPlan(run, scheme) is not { } plan)
                            continue;

                        _effectRuns.Add(run);
                        if (budget <= 0)
                            continue;

                        var lines = BuildGlyphLines(run, bounds, pixelsPerDip, ref budget);
                        if (lines.Count == 0)
                            continue;

                        var glyphBrush = plan.Fill is { IsNone: true } ? null : run.Foreground;
                        TextEffectsPainter.Paint(plan, lines, glyphBrush, topContext, blurred.Add);
                    }
                }

                foreach (var layer in blurred)
                    _layers.Add(layer);
                _layers.Add(top);
                _paintedSignature = CurrentSignature();
            }
            finally
            {
                _refreshing = false;
            }
        }

        /// <summary>
        /// What decides where the effects go: the view's size, the page geometry and zoom, and where the
        /// first and last effect runs are laid out. Two character-rect queries, so cheap enough for every
        /// layout pass.
        /// </summary>
        private LayoutSignature CurrentSignature()
        {
            var document = _view.Document;
            Rect first = Rect.Empty, last = Rect.Empty;
            if (_effectRuns.Count > 0 && document is not null)
            {
                try
                {
                    var head = _effectRuns[0];
                    var tail = _effectRuns[^1];
                    if (head.ContentStart.IsInSameDocument(document.ContentStart)
                        && tail.ContentEnd.IsInSameDocument(document.ContentStart))
                    {
                        first = head.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                        last = tail.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Layout momentarily unavailable: leave the rects empty so the next pass compares again.
                }
            }

            return new LayoutSignature(
                _view.ActualWidth,
                _view.ActualHeight,
                _view.ZoomLevel,
                document?.PageWidth ?? 0,
                document?.PagePadding ?? default,
                first,
                last);
        }

        private readonly record struct LayoutSignature(
            double Width,
            double Height,
            double Zoom,
            double PageWidth,
            Thickness PagePadding,
            Rect First,
            Rect Last);

        /// <summary>
        /// The run's glyph outlines, one geometry per laid-out line. Each glyph is shaped on its own and
        /// placed at the position the editor laid it out at, so justification, kerning and wrapping in
        /// the live layout are followed exactly.
        /// </summary>
        private static List<TextEffectsPainter.GlyphLine> BuildGlyphLines(WpfRun run, Rect bounds, double pixelsPerDip, ref int budget)
        {
            var lines = new List<TextEffectsPainter.GlyphLine>();
            var text = run.Text;
            if (string.IsNullOrEmpty(text))
                return lines;

            var typeface = new Typeface(run.FontFamily, run.FontStyle, run.FontWeight, run.FontStretch);
            var rightToLeft = run.FlowDirection == FlowDirection.RightToLeft;
            TextEffectsPainter.GlyphLine? current = null;
            for (var index = 0; index < text.Length && budget > 0;)
            {
                var length = char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? 2 : 1;
                var glyph = text.Substring(index, length);
                var startOffset = index;
                index += length;
                if (string.IsNullOrWhiteSpace(glyph) || glyph == "­")
                    continue;

                Rect leading, trailing;
                try
                {
                    leading = run.ContentStart.GetPositionAtOffset(startOffset)?.GetCharacterRect(LogicalDirection.Forward) ?? Rect.Empty;
                    trailing = run.ContentStart.GetPositionAtOffset(index)?.GetCharacterRect(LogicalDirection.Backward) ?? Rect.Empty;
                }
                catch (InvalidOperationException)
                {
                    return lines; // layout momentarily unavailable
                }

                if (leading.IsEmpty || leading.Bottom < bounds.Top - 200 || leading.Top > bounds.Bottom + 200)
                    continue;

                budget--;
                var formatted = new FormattedText(
                    glyph,
                    CultureInfo.CurrentUICulture,
                    rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                    typeface,
                    run.FontSize,
                    Brushes.Black,
                    pixelsPerDip);
                var sameLine = !trailing.IsEmpty && Math.Abs(trailing.Top - leading.Top) < 0.5;
                var left = sameLine
                    ? Math.Min(leading.X, trailing.X)
                    : rightToLeft ? leading.X - formatted.WidthIncludingTrailingWhitespace : leading.X;
                // A right-to-left FormattedText draws leftwards from its origin.
                var origin = new Point(rightToLeft ? left + formatted.WidthIncludingTrailingWhitespace : left, leading.Top);
                var geometry = formatted.BuildGeometry(origin);

                if (current is null || Math.Abs(current.Top - leading.Top) >= 0.5)
                {
                    current = new TextEffectsPainter.GlyphLine(leading.Top, leading.Bottom, left);
                    lines.Add(current);
                }

                current.Add(geometry, leading.Bottom, left);
            }

            foreach (var line in lines)
                line.Geometry.Freeze();
            return lines;
        }

        internal static IEnumerable<WpfRun> EnumerateRuns(IEnumerable<System.Windows.Documents.Block> blocks)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case WpfParagraph paragraph:
                        foreach (var run in EnumerateInlines(paragraph.Inlines))
                            yield return run;
                        break;
                    case WpfList list:
                        foreach (var item in list.ListItems)
                            foreach (var run in EnumerateRuns(item.Blocks))
                                yield return run;
                        break;
                    case WpfTable table:
                        foreach (var group in table.RowGroups)
                            foreach (var row in group.Rows)
                                foreach (var cell in row.Cells)
                                    foreach (var run in EnumerateRuns(cell.Blocks))
                                        yield return run;
                        break;
                    case System.Windows.Documents.Section section:
                        foreach (var run in EnumerateRuns(section.Blocks))
                            yield return run;
                        break;
                }
            }
        }

        private static IEnumerable<WpfRun> EnumerateInlines(InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                if (inline is WpfRun run)
                {
                    yield return run;
                }
                else if (inline is Span span)
                {
                    foreach (var inner in EnumerateInlines(span.Inlines))
                        yield return inner;
                }
            }
        }
    }
}
