using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// Slide thumbnail / sorter pane (Wave 3B).
///
/// Displays a vertical, scrollable list of slide thumbnails.  Each item shows a slide-number
/// label and a small <see cref="SlideCanvas"/> (150 px wide, 16:9 height, display-only).
/// Clicking an item calls <see cref="EditingSession.SelectSlide"/>.
///
/// The pane rebuilds / refreshes when:
///   - <see cref="EditingSession.Changed"/> fires (slide added/removed/edited/reordered)
///   - <see cref="EditingSession.CurrentSlideChanged"/> fires (highlight update only)
///
/// Drag-to-reorder: drag a thumbnail up/down; a 2 px insertion-indicator line shows the
/// target position; dropping calls <see cref="EditingSession.MoveSlide"/>.
///
/// Context menu on each item:
///   New Slide (insert after), Duplicate Slide, Delete Slide.
/// A "New Slide" button is always present at the bottom of the list.
/// </summary>
public sealed class SlidePane : Border
{
    // ── Colors ────────────────────────────────────────────────────────────────────

    private static readonly Brush BackgroundBrush    = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)));
    private static readonly Brush ItemNormalBg       = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)));
    private static readonly Brush ItemSelectedBg     = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xD6)));
    private static readonly Brush ItemHoverBg        = Freeze(new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB)));
    private static readonly Brush ItemSelectedBorder = Freeze(new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)));
    private static readonly Brush ItemNormalBorder   = Freeze(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));
    private static readonly Brush LabelBrush         = Freeze(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)));
    private static readonly Brush InsertLineBrush    = Freeze(new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)));

    // Thumbnail dimensions
    private const double ThumbWidth  = 150.0;
    private const double ThumbHeight = ThumbWidth * 9.0 / 16.0; // ~84.4 px
    private const double ItemPadding = 8.0;
    private const double LabelHeight = 16.0;
    // Total item height (top margin 4 + padding + label + padding + thumb + padding + bottom margin 4)
    private const double ItemHeight  = 4 + ItemPadding + LabelHeight + 4 + ThumbHeight + ItemPadding + 4;

    // ── Fields ────────────────────────────────────────────────────────────────────

    private readonly EditingSession _editor;
    private readonly ScrollViewer   _scroll;
    private readonly StackPanel     _stack;

    // Insertion indicator: a thin horizontal line drawn over the scroll area.
    private readonly Border _insertIndicator;

    // Drag state
    private bool   _isDragging;
    private int    _dragSourceIndex = -1;
    private int    _dragTargetIndex = -1;  // insertion point (0 = before slide 0)
    private Point  _dragStartPoint;

    // ── Construction ──────────────────────────────────────────────────────────────

    public SlidePane(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));

        Background = BackgroundBrush;

        _stack = new StackPanel { Orientation = Orientation.Vertical };

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content                       = _stack
        };

        // Insertion-indicator line (hidden by default).
        _insertIndicator = new Border
        {
            Height              = 2,
            Background          = InsertLineBrush,
            Visibility          = Visibility.Collapsed,
            IsHitTestVisible    = false,
            VerticalAlignment   = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // Overlay: Grid with ScrollViewer in row 0 (stretch) and the indicator absolutely
        // positioned via top-margin in the same row.
        var overlay = new Grid();
        overlay.Children.Add(_scroll);
        overlay.Children.Add(_insertIndicator);

        Child = overlay;

        // Subscribe to model events.
        _editor.Changed             += OnChanged;
        _editor.CurrentSlideChanged += OnCurrentSlideChanged;

        // Initial build.
        RebuildList();
    }

    // ── Event handling ────────────────────────────────────────────────────────────

    private void OnChanged() => RebuildList();

    private void OnCurrentSlideChanged(object? sender, EventArgs e) => UpdateHighlight();

    // ── List build ────────────────────────────────────────────────────────────────

    /// <summary>Fully rebuilds the item list. Called on structural changes.</summary>
    private void RebuildList()
    {
        _stack.Children.Clear();

        var slides = _editor.Presentation.Slides;
        for (int i = 0; i < slides.Count; i++)
        {
            var item = BuildSlideItem(i, slides[i]);
            _stack.Children.Add(item);
        }

        // "New Slide" affordance at the bottom.
        _stack.Children.Add(BuildNewSlideButton());
    }

    /// <summary>Updates only the highlight (selected border/background) on existing items.
    /// Called on selection-only changes — avoids a full rebuild.</summary>
    private void UpdateHighlight()
    {
        int idx = _editor.CurrentSlideIndex;
        for (int i = 0; i < _stack.Children.Count; i++)
        {
            if (_stack.Children[i] is not Border item) continue;
            if (item.Tag is not int itemIdx) continue;
            bool selected = itemIdx == idx;
            item.BorderBrush     = selected ? ItemSelectedBorder : ItemNormalBorder;
            item.BorderThickness = selected ? new Thickness(2)   : new Thickness(1);
            item.Background      = selected ? ItemSelectedBg      : ItemNormalBg;
        }
    }

    // ── Item construction ─────────────────────────────────────────────────────────

    private Border BuildSlideItem(int index, Slide slide)
    {
        bool selected = index == _editor.CurrentSlideIndex;

        // Slide-number label.
        var label = new TextBlock
        {
            Text                = (index + 1).ToString(),
            FontSize            = 11,
            Foreground          = LabelBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Height              = LabelHeight,
            VerticalAlignment   = VerticalAlignment.Center,
            Margin              = new Thickness(0, 0, 0, 4)
        };

        // Thumbnail canvas (display-only, non-interactive).
        var thumb = new SlideCanvas
        {
            Width            = ThumbWidth,
            Height           = ThumbHeight,
            Presentation     = _editor.Presentation,
            Slide            = slide,
            IsHitTestVisible = false,
            IsEnabled        = false
        };

        var thumbBorder = new Border
        {
            BorderBrush     = ItemNormalBorder,
            BorderThickness = new Thickness(1),
            Child           = thumb
        };

        var panel = new StackPanel
        {
            Orientation         = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(label);
        panel.Children.Add(thumbBorder);

        var item = new Border
        {
            Tag             = index,
            Background      = selected ? ItemSelectedBg     : ItemNormalBg,
            BorderBrush     = selected ? ItemSelectedBorder : ItemNormalBorder,
            BorderThickness = selected ? new Thickness(2)   : new Thickness(1),
            CornerRadius    = new CornerRadius(3),
            Margin          = new Thickness(6, 4, 6, 4),
            Padding         = new Thickness(ItemPadding),
            Child           = panel,
            Cursor          = Cursors.Hand
        };

        // Click -> SelectSlide.
        item.MouseLeftButtonDown += (sender, e) =>
        {
            if (sender is Border b && b.Tag is int idx)
            {
                _dragStartPoint = e.GetPosition(b);
                _editor.SelectSlide(idx);
                e.Handled = true;
            }
        };

        // Hover effect.
        item.MouseEnter += (sender, e) =>
        {
            if (sender is Border b && b.Tag is int idx && idx != _editor.CurrentSlideIndex)
                b.Background = ItemHoverBg;
        };
        item.MouseLeave += (sender, e) =>
        {
            if (sender is Border b && b.Tag is int idx)
                b.Background = idx == _editor.CurrentSlideIndex ? ItemSelectedBg : ItemNormalBg;
        };

        // Drag-to-reorder.
        item.MouseMove         += OnItemMouseMove;
        item.MouseLeftButtonUp += OnItemMouseLeftButtonUp;

        // Context menu.
        item.ContextMenu = BuildContextMenu(index);

        return item;
    }

    private Button BuildNewSlideButton()
    {
        var btn = new Button
        {
            Content             = "+ New Slide",
            Margin              = new Thickness(12, 8, 12, 12),
            Padding             = new Thickness(0, 6, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background          = Freeze(new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A))),
            Foreground          = Brushes.White,
            BorderThickness     = new Thickness(0),
            FontSize            = 12,
            Cursor              = Cursors.Hand
        };
        btn.Click += (_, _) => _editor.InsertSlide();
        return btn;
    }

    private ContextMenu BuildContextMenu(int index)
    {
        var menu = new ContextMenu();

        var newItem = new MenuItem { Header = "New Slide" };
        newItem.Click += (_, _) =>
        {
            _editor.SelectSlide(index);
            _editor.InsertSlide();
        };

        var dupItem = new MenuItem { Header = "Duplicate Slide" };
        dupItem.Click += (_, _) =>
        {
            _editor.SelectSlide(index);
            _editor.DuplicateCurrentSlide();
        };

        var delItem = new MenuItem { Header = "Delete Slide" };
        delItem.Click += (_, _) =>
        {
            _editor.SelectSlide(index);
            _editor.DeleteCurrentSlide();
        };

        menu.Items.Add(newItem);
        menu.Items.Add(dupItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(delItem);

        return menu;
    }

    // ── Drag-to-reorder ───────────────────────────────────────────────────────────

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not Border item || item.Tag is not int sourceIdx) return;

        if (!_isDragging)
        {
            // Start drag only after a minimum Y distance.
            var pos = e.GetPosition(item);
            if (Math.Abs(pos.Y - _dragStartPoint.Y) < 5) return;

            _isDragging      = true;
            _dragSourceIndex = sourceIdx;
            item.CaptureMouse();
        }

        // Determine insertion point from Y relative to _stack.
        var posInStack = e.GetPosition(_stack);
        _dragTargetIndex = HitTestInsertionPoint(posInStack.Y);
        ShowInsertIndicator();
        e.Handled = true;
    }

    private void OnItemMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border item) return;

        if (_isDragging)
        {
            item.ReleaseMouseCapture();
            _isDragging = false;
            HideInsertIndicator();

            int from = _dragSourceIndex;
            int to   = _dragTargetIndex;

            // MoveSlide(from, to): skip no-op moves.
            if (from >= 0 && to >= 0 && to != from && to != from + 1)
                _editor.MoveSlide(from, to);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Returns insertion index (0 = before slide 0, N = after last slide)
    /// based on Y coordinate relative to the StackPanel.
    /// </summary>
    private int HitTestInsertionPoint(double y)
    {
        int count = SlideItemCount();
        for (int i = 0; i < count; i++)
        {
            double midY = (i + 0.5) * ItemHeight;
            if (y < midY) return i;
        }
        return count;
    }

    private int SlideItemCount() =>
        // Last child of _stack is the "New Slide" button — exclude it.
        Math.Max(0, _stack.Children.Count - 1);

    private void ShowInsertIndicator()
    {
        int count = SlideItemCount();
        double indicatorY = _dragTargetIndex >= count
            ? count * ItemHeight
            : _dragTargetIndex * ItemHeight;

        _insertIndicator.Margin     = new Thickness(0, indicatorY - 1, 0, 0);
        _insertIndicator.Visibility = Visibility.Visible;
    }

    private void HideInsertIndicator() =>
        _insertIndicator.Visibility = Visibility.Collapsed;

    // ── Static helpers ────────────────────────────────────────────────────────────

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }
}
