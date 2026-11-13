using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Host.Editing;

/// <summary>
/// W11: the markup the continuous editor draws over its text, on the pages: grammar underlines (blue
/// double underline) with their right-click menu, and review balloons (comments and tracked changes) in a
/// strip beside each page. Both come from the hidden source editor's model-level diagnostics and
/// balloon sources, mapped onto the pages through the same document positions the selection bridge uses,
/// so they are refreshed whenever the pages and the model agree (after a commit or re-flow).
/// </summary>
internal sealed partial class PaginatedEditorPanel
{
    private static readonly TimeSpan MarkupDelay = TimeSpan.FromMilliseconds(350);

    private readonly Dictionary<PageBox, List<TextRange>> _pageGrammarRanges = new();
    private readonly Dictionary<PageBox, GrammarSquiggleAdorner> _pageGrammarAdorners = new();
    private readonly Dictionary<PageBox, PageBalloonAdorner> _pageBalloonAdorners = new();
    private readonly List<PageBalloon> _pageBalloons = [];
    private DispatcherTimer? _markupTimer;
    private ContextMenu? _pageGrammarMenu;

    /// <summary>A review balloon placed beside a page, anchored at <see cref="AnchorY"/> in the page body.</summary>
    internal sealed record PageBalloon(PageBox Box, ReviewBalloonSource Source, double AnchorY);

    /// <summary>The text under each grammar underline on <paramref name="box"/>, in order.</summary>
    internal IReadOnlyList<string> GrammarSquiggleTexts(PageBox box) =>
        _pageGrammarRanges.TryGetValue(box, out var ranges)
            ? ranges.Select(range => range.Text).ToArray()
            : [];

    /// <summary>The review balloons shown beside the pages (when Show Markup &gt; Balloons is on).</summary>
    internal IReadOnlyList<PageBalloon> PageBalloons => _pageBalloons;

    /// <summary>Recomputes the page markup once the pages have been quiet for a moment.</summary>
    internal void ScheduleMarkupRefresh()
    {
        if (_markupTimer is null)
        {
            _markupTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = MarkupDelay };
            _markupTimer.Tick += (_, _) =>
            {
                _markupTimer.Stop();
                try
                {
                    RefreshPageMarkup();
                }
                catch
                {
                    // Chrome only: a failed refresh leaves the previous markup in place.
                }
            };
        }
        _markupTimer.Stop();
        _markupTimer.Start();
    }

    /// <summary>Recomputes the grammar underlines and balloons of the pages shown (when they agree with the model).</summary>
    internal void RefreshPageMarkup()
    {
        _markupTimer?.Stop();
        if (!SourceMatchesPages())
            return;
        var logicalCount = _pageBoxes.Sum(LogicalBlockCount);
        RefreshPageGrammarSquiggles(logicalCount);
        RefreshPageBalloons(logicalCount);
        SyncMarkupAdorners();
    }

    /// <summary>The page position of a model text position, when that position is on a page shown.</summary>
    private (PageBox Box, TextPointer Pointer)? PagePositionOfModelText(int blockIndex, int offset, int logicalCount)
    {
        if (_sourceEditor.PositionAtModelTextOffset(blockIndex, offset) is not { } source
            || SourcePosition(source) is not { } global
            || global.BlockIndex >= logicalCount)
        {
            return null;
        }
        return ResolveGlobal(global);
    }

    /// <summary>The model blocks the realised pages show (their underlines and balloons are the ones drawn).</summary>
    private Func<int, bool> ShownModelBlocks()
    {
        if (!_boxRangesValid)
            return _ => true;
        var ranges = _pageBoxes
            .Where(box => box.IsRealized && box.ModelBlocks is not null)
            .Select(box => (From: box.ModelStart - (box.ContinuesPrevious ? 1 : 0), To: box.ModelStart + box.ModelBlocks!.Length))
            .ToList();
        return block => ranges.Any(range => block >= range.From && block < range.To);
    }

    private void RefreshPageGrammarSquiggles(int logicalCount)
    {
        _pageGrammarRanges.Clear();
        if (!_sourceEditor.SpellCheckEnabled)
            return;
        var shown = ShownModelBlocks();
        foreach (var diagnostic in _sourceEditor.VisibleGrammarDiagnostics())
        {
            if (!shown(diagnostic.BlockIndex))
                continue;
            if (PagePositionOfModelText(diagnostic.BlockIndex, diagnostic.ParagraphOffset, logicalCount) is not { } start
                || PagePositionOfModelText(diagnostic.BlockIndex, diagnostic.ParagraphOffset + diagnostic.Length, logicalCount) is not { } end)
            {
                continue;
            }

            if (ReferenceEquals(start.Box, end.Box))
            {
                var range = new TextRange(start.Pointer, end.Pointer);
                if (string.Equals(range.Text, diagnostic.Word, StringComparison.Ordinal))
                    AddGrammarRange(start.Box, range);
                continue;
            }

            // A flagged phrase the page boundary splits: underline its part on each page.
            AddGrammarRange(start.Box, new TextRange(start.Pointer, start.Box.Body.Document.ContentEnd));
            AddGrammarRange(end.Box, new TextRange(end.Box.Body.Document.ContentStart, end.Pointer));
        }
    }

    private void AddGrammarRange(PageBox box, TextRange range)
    {
        if (!_pageGrammarRanges.TryGetValue(box, out var list))
            _pageGrammarRanges[box] = list = [];
        list.Add(range);
    }

    private void RefreshPageBalloons(int logicalCount)
    {
        _pageBalloons.Clear();
        if (!_sourceEditor.ShowMarkupBalloons)
            return;
        var shown = ShownModelBlocks();
        foreach (var source in ReviewBalloonLayoutPlanner.BuildSources(_sourceModel, _sourceEditor.CurrentReviewDisplayPolicy))
        {
            if (!shown(source.BlockIndex))
                continue;
            if (PagePositionOfModelText(source.BlockIndex, source.Offset, logicalCount) is { } position)
            {
                _pageBalloons.Add(new PageBalloon(position.Box, source, AnchorY(position.Box, position.Pointer)));
                continue;
            }

            // A table or other block without a text position: anchor at the top of the page it starts on.
            if (_pageBoxes.FirstOrDefault(box => box.ModelBlocks is { } blocks
                    && source.BlockIndex >= box.ModelStart && source.BlockIndex < box.ModelStart + blocks.Length) is { } owner)
            {
                _pageBalloons.Add(new PageBalloon(owner, source, owner.Body.Padding.Top));
            }
        }
    }

    private static double AnchorY(PageBox box, TextPointer pointer)
    {
        try
        {
            var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
            return rect.IsEmpty ? box.Body.Padding.Top : rect.Top + rect.Height / 2;
        }
        catch (InvalidOperationException)
        {
            return box.Body.Padding.Top;
        }
    }

    /// <summary>Puts the underline and balloon adorners on the realised pages that need them (and off the others).</summary>
    private void SyncMarkupAdorners()
    {
        foreach (var (box, adorner) in _pageGrammarAdorners.ToList())
        {
            if (_pageBoxes.Contains(box) && box.IsRealized && _pageGrammarRanges.ContainsKey(box))
            {
                adorner.InvalidateVisual();
                continue;
            }
            AdornerLayer.GetAdornerLayer(box.Body)?.Remove(adorner);
            _pageGrammarAdorners.Remove(box);
        }
        foreach (var box in _pageGrammarRanges.Keys)
        {
            if (!box.IsRealized || _pageGrammarAdorners.ContainsKey(box) || AdornerLayer.GetAdornerLayer(box.Body) is not { } layer)
                continue;
            var captured = box;
            var adorner = new GrammarSquiggleAdorner(box.Body,
                () => _pageGrammarRanges.TryGetValue(captured, out var ranges) ? ranges : []);
            layer.Add(adorner);
            _pageGrammarAdorners[box] = adorner;
        }

        // Balloons: the page gets room on its right for its strip while balloons are shown.
        var balloonsOn = _sourceEditor.ShowMarkupBalloons;
        foreach (var box in _pageBoxes)
        {
            var margin = box.Margin;
            var right = balloonsOn ? BalloonOverlay.StripBoxWidth : (_pageHost is Grid ? 12 : 0);
            if (!margin.Right.Equals(right))
                box.Margin = new Thickness(margin.Left, margin.Top, right, margin.Bottom);
        }
        foreach (var (box, adorner) in _pageBalloonAdorners.ToList())
        {
            AdornerLayer.GetAdornerLayer(box)?.Remove(adorner);
            _pageBalloonAdorners.Remove(box);
        }
        foreach (var group in _pageBalloons.GroupBy(balloon => balloon.Box))
        {
            if (!group.Key.IsRealized || AdornerLayer.GetAdornerLayer(group.Key) is not { } layer)
                continue;
            var adorner = new PageBalloonAdorner(group.Key, group.ToList());
            layer.Add(adorner);
            _pageBalloonAdorners[group.Key] = adorner;
        }
    }

    // ── grammar right-click menu ───────────────────────────────────────────────────────────────────

    private void HookMarkup(PageBox box)
    {
        box.Body.PreviewMouseRightButtonDown += OnPageBodyPreviewRightButtonDown;
        box.Body.PreviewKeyDown += OnPageBodyPreviewMenuKey;
    }

    private void UnhookMarkup(PageBox box)
    {
        box.Body.PreviewMouseRightButtonDown -= OnPageBodyPreviewRightButtonDown;
        box.Body.PreviewKeyDown -= OnPageBodyPreviewMenuKey;
        if (_pageGrammarAdorners.Remove(box, out var adorner))
            AdornerLayer.GetAdornerLayer(box.Body)?.Remove(adorner);
        if (_pageBalloonAdorners.Remove(box, out var balloons))
            AdornerLayer.GetAdornerLayer(box)?.Remove(balloons);
        _pageGrammarRanges.Remove(box);
    }

    private void OnPageBodyPreviewRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (BoxOf(sender) is { } box)
            PreparePageGrammarMenu(box, box.Body.GetPositionFromPoint(e.GetPosition(box.Body), snapToText: false));
    }

    private void OnPageBodyPreviewMenuKey(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Apps || (e.SystemKey == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift))
            && BoxOf(sender) is { } box)
        {
            PreparePageGrammarMenu(box, box.Body.CaretPosition);
        }
    }

    private PageBox? BoxOf(object sender) =>
        sender is RichTextBox body ? _pageBoxes.FirstOrDefault(box => ReferenceEquals(box.Body, body)) : null;

    /// <summary>
    /// Before a page's context menu opens: over a grammar underline the page shows the continuous editor's
    /// grammar menu (rule, suggestions, Ignore Once, Don't Check for This Issue, clipboard commands acting
    /// on the page); anywhere else the native editing/spelling menu.
    /// </summary>
    internal void PreparePageGrammarMenu(PageBox box, TextPointer? position)
    {
        if (ReferenceEquals(box.Body.ContextMenu, _pageGrammarMenu))
            box.Body.ContextMenu = null;
        _pageGrammarMenu = null;

        if (position is null || (box.Body.SpellCheck.IsEnabled && box.Body.GetSpellingError(position) is not null))
            return;
        if (!_pageGrammarRanges.TryGetValue(box, out var ranges)
            || !ranges.Any(range => range.Start.IsInSameDocument(position) && range.Contains(position)))
        {
            return;
        }

        if (ToGlobal(box, position) is not { } global)
            return;
        PrepareForCommand();
        if (!SourceMatchesPages() || SourcePointer(global) is not { } sourcePosition
            || _sourceEditor.GrammarIssueAt(sourcePosition) is not { } issue)
        {
            return;
        }

        var menu = _sourceEditor.BuildGrammarContextMenu(issue);
        foreach (var item in menu.Items.OfType<MenuItem>().Where(item => item.Command is not null))
            item.CommandTarget = box.Body;
        _pageGrammarMenu = menu;
        box.Body.ContextMenu = menu;
    }

    /// <summary>The menu the last right-click over a page prepared (null: the native one).</summary>
    internal ContextMenu? PageGrammarMenu => _pageGrammarMenu;

    /// <summary>
    /// The balloons of one page, drawn beside it: stacked from their anchors downward without overlapping,
    /// each with a dashed leader back to the page's right edge at its anchor.
    /// </summary>
    private sealed class PageBalloonAdorner : Adorner
    {
        private readonly Canvas _canvas = new() { IsHitTestVisible = false };

        public PageBalloonAdorner(PageBox page, IReadOnlyList<PageBalloon> balloons) : base(page)
        {
            IsHitTestVisible = false;
            AddVisualChild(_canvas);
            var width = page.Width > 0 ? page.Width : page.ActualWidth;
            var bodyTop = page.Body.TranslatePoint(new Point(0, 0), page).Y;
            var next = 8.0;
            foreach (var balloon in balloons.OrderBy(balloon => balloon.AnchorY))
            {
                var anchorY = bodyTop + balloon.AnchorY;
                var y = Math.Max(next, anchorY - BalloonOverlay.BalloonBoxHeight / 2);
                BalloonOverlay.Draw(_canvas, balloon.Source, leaderStartX: width, x: width + 12, y, anchorY);
                next = y + BalloonOverlay.BalloonBoxHeight + 8;
            }
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _canvas;

        protected override Size ArrangeOverride(Size finalSize)
        {
            _canvas.Arrange(new Rect(finalSize));
            return finalSize;
        }

        protected override Size MeasureOverride(Size constraint)
        {
            _canvas.Measure(constraint);
            return base.MeasureOverride(constraint);
        }
    }
}
