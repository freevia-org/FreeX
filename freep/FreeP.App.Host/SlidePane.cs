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
/// Wave 11B: when the presentation has sections a section-header row (section name + slide
/// count) is injected above the first thumbnail belonging to each section.
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

    private static readonly Brush BackgroundBrush    = BrushFromHex(SlidePanePlanner.DefaultPaneBackgroundHex);
    private static readonly Brush ItemNormalBg       = BrushFromHex(SlidePanePlanner.DefaultItemNormalBackgroundHex);
    private static readonly Brush ItemSelectedBg     = BrushFromHex(SlidePanePlanner.DefaultItemSelectedBackgroundHex);
    private static readonly Brush ItemHoverBg        = BrushFromHex(SlidePanePlanner.DefaultItemHoverBackgroundHex);
    private static readonly Brush ItemSelectedBorder = BrushFromHex(SlidePanePlanner.DefaultItemSelectedBorderHex);
    private static readonly Brush ItemNormalBorder   = BrushFromHex(SlidePanePlanner.DefaultItemNormalBorderHex);
    private static readonly Brush LabelBrush         = BrushFromHex(SlidePanePlanner.DefaultLabelForegroundHex);
    private static readonly Brush InsertLineBrush    = BrushFromHex(SlidePanePlanner.DefaultDropIndicatorAccentHex);

    // Section header row colors (Wave 11B)
    private static readonly Brush SectionHeaderBg   = Freeze(new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)));
    private static readonly Brush SectionHeaderFg   = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)));

    private const double ItemHeight = SlidePanePlanner.DefaultSlideItemHeight;

    // ── Fields ────────────────────────────────────────────────────────────────────

    private readonly EditingSession _editor;
    private readonly ScrollViewer   _scroll;
    private readonly StackPanel     _stack;
    private readonly HashSet<string> _collapsedSectionIds = new(StringComparer.OrdinalIgnoreCase);

    // Insertion indicator: a thin horizontal line drawn over the scroll area.
    private readonly Border _insertIndicator;

    // Drag state
    private bool   _isDragging;
    private int    _dragSourceIndex = -1;
    private int    _dragTargetIndex = -1;  // insertion point (0 = before slide 0)
    private Point  _dragStartPoint;

    private sealed record SectionHeaderTag(string SectionId, int SectionIndex);

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
            Height              = SlidePanePlanner.DefaultDropIndicatorThickness,
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
        var entries = SlidePanePlanner.BuildEntries(slides, _editor.Presentation.Sections, _collapsedSectionIds);

        foreach (var entry in entries)
        {
            if (entry.Kind == SlidePaneEntryKind.SectionHeader)
            {
                _stack.Children.Add(BuildSectionHeader(entry));
                continue;
            }

            var plan = SlidePanePlanner.BuildThumbnailVisualPlan(
                entry,
                slides[entry.SlideIndex],
                _editor.CurrentSlideIndex);
            var item = BuildSlideItem(plan, slides[entry.SlideIndex]);
            _stack.Children.Add(item);
        }

        // "New Slide" affordance at the bottom.
        _stack.Children.Add(BuildNewSlideButton());
    }

    /// <summary>
    /// Builds an interactive section-header row showing the section name and slide count.
    /// Wave 11B.
    /// </summary>
    private Border BuildSectionHeader(SlidePaneEntry entry)
    {
        var disclosure = new TextBlock
        {
            Text              = entry.IsSectionCollapsed ? ">" : "v",
            FontSize          = 11,
            FontWeight        = FontWeights.Bold,
            Foreground        = SectionHeaderFg,
            Width             = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var label = new TextBlock
        {
            Text                = entry.Text,
            FontSize            = 11,
            FontWeight          = FontWeights.SemiBold,
            Foreground          = SectionHeaderFg,
            VerticalAlignment   = VerticalAlignment.Center,
            TextTrimming        = TextTrimming.CharacterEllipsis,
        };

        var panel = new DockPanel();
        DockPanel.SetDock(disclosure, Dock.Left);
        panel.Children.Add(disclosure);
        panel.Children.Add(label);

        var header = new Border
        {
            Background      = SectionHeaderBg,
            Padding         = new Thickness(10, 4, 10, 4),
            Margin          = new Thickness(0, 6, 0, 2),
            Tag             = new SectionHeaderTag(entry.SectionId, entry.SectionIndex),
            ContextMenu     = BuildSectionContextMenu(entry),
            Child           = panel,
            Cursor          = Cursors.Hand,
            Focusable       = true,
            ToolTip         = entry.IsSectionCollapsed ? "Expand section" : "Collapse section",
        };
        header.MouseLeftButtonDown += (_, e) =>
        {
            ToggleSection(entry.SectionId);
            e.Handled = true;
        };
        header.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                ToggleSection(entry.SectionId);
                e.Handled = true;
            }
        };

        return header;
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
            item.BorderThickness = selected
                ? new Thickness(SlidePanePlanner.DefaultSelectedBorderThickness)
                : new Thickness(SlidePanePlanner.DefaultNormalBorderThickness);
            item.Background      = selected ? ItemSelectedBg      : ItemNormalBg;
        }
    }

    // ── Item construction ─────────────────────────────────────────────────────────

    private Border BuildSlideItem(SlidePaneThumbnailVisualPlan plan, Slide slide)
    {
        // Slide-number label.
        var label = new TextBlock
        {
            Text                = plan.LabelText,
            FontSize            = 11,
            Foreground          = LabelBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Height              = plan.LabelHeight,
            VerticalAlignment   = VerticalAlignment.Center,
            Margin              = new Thickness(0, 0, 0, 4)
        };

        // Thumbnail canvas (display-only, non-interactive).
        var thumb = new SlideCanvas
        {
            Width            = plan.ThumbnailWidth,
            Height           = plan.ThumbnailHeight,
            Presentation     = _editor.Presentation,
            Slide            = slide,
            IsHitTestVisible = false,
            IsEnabled        = false
        };

        var thumbBorder = new Border
        {
            BorderBrush     = BrushFromHex(plan.ThumbnailBorderHex),
            BorderThickness = new Thickness(plan.NormalBorderThickness),
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
            Tag             = plan.SlideIndex,
            Background      = BrushFromHex(plan.IsSelected ? plan.ItemSelectedBackgroundHex : plan.ItemNormalBackgroundHex),
            BorderBrush     = BrushFromHex(plan.IsSelected ? plan.ItemSelectedBorderHex : plan.ItemNormalBorderHex),
            BorderThickness = new Thickness(plan.IsSelected ? plan.SelectedBorderThickness : plan.NormalBorderThickness),
            CornerRadius    = new CornerRadius(plan.ItemCornerRadius),
            Margin          = new Thickness(6, 4, 6, 4),
            Padding         = new Thickness(plan.ItemPadding),
            Child           = panel,
            Cursor          = Cursors.Hand,
            ToolTip         = plan.ToolTipText
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
                b.Background = BrushFromHex(plan.ItemHoverBackgroundHex);
        };
        item.MouseLeave += (sender, e) =>
        {
            if (sender is Border b && b.Tag is int idx)
                b.Background = BrushFromHex(idx == _editor.CurrentSlideIndex
                    ? plan.ItemSelectedBackgroundHex
                    : plan.ItemNormalBackgroundHex);
        };

        // Drag-to-reorder.
        item.MouseMove         += OnItemMouseMove;
        item.MouseLeftButtonUp += OnItemMouseLeftButtonUp;

        // Context menu.
        item.ContextMenu = BuildContextMenu(plan.SlideIndex);

        return item;
    }

    private Button BuildNewSlideButton()
    {
        var btn = new Button
        {
            Content             = SlidePanePlanner.NewSlideButtonText,
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

        foreach (var action in SlideSectionPlanner.BuildSlideContextActions(
                     _editor.Presentation.Slides,
                     _editor.Presentation.Sections,
                     index))
        {
            var item = new MenuItem
            {
                Header = action.Text,
                IsEnabled = action.IsEnabled,
            };
            item.Click += (_, _) => ApplySectionAction(action);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        foreach (var action in SlidePanePlanner.BuildContextActions(_editor.Presentation.Slides.Count, index))
        {
            if (action.Kind == SlidePaneActionKind.DeleteSlide)
                menu.Items.Add(new Separator());

            var item = new MenuItem
            {
                Header = action.Text,
                IsEnabled = action.IsEnabled,
            };
            item.Click += (_, _) => SlidePanePlanner.TryApplyAction(_editor, action);
            menu.Items.Add(item);
        }

        return menu;
    }

    private ContextMenu BuildSectionContextMenu(SlidePaneEntry entry)
    {
        var menu = new ContextMenu();

        foreach (var action in SlideSectionPlanner.BuildSectionHeaderActions(
                     _editor.Presentation.Sections,
                     entry.SectionIndex,
                     entry.SlideIndex))
        {
            if (action.Kind == SlideSectionActionKind.RemoveSection)
                menu.Items.Add(new Separator());

            var item = new MenuItem
            {
                Header = action.Text,
                IsEnabled = action.IsEnabled,
            };
            item.Click += (_, _) => ApplySectionAction(action);
            menu.Items.Add(item);
        }

        return menu;
    }

    private void ToggleSection(string sectionId)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
            return;

        if (!_collapsedSectionIds.Add(sectionId))
            _collapsedSectionIds.Remove(sectionId);

        RebuildList();
    }

    internal int SlidePaneSlideItemCount => _stack.Children
        .OfType<Border>()
        .Count(child => child.Tag is int);

    internal int SlidePaneSectionHeaderCount => _stack.Children
        .OfType<Border>()
        .Count(child => child.Tag is SectionHeaderTag);

    internal bool ToggleSectionForTests(int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= _editor.Presentation.Sections.Count)
            return false;

        ToggleSection(SlidePanePlanner.GetSectionIdentity(_editor.Presentation.Sections[sectionIndex], sectionIndex));
        return true;
    }

    private void ApplySectionAction(SlideSectionActionPlan action)
    {
        var execution = SlideSectionPlanner.BuildExecutionPlan(action);
        if (!execution.IsEnabled)
            return;

        string? promptedName = null;
        if (execution.RequiresNamePrompt)
        {
            promptedName = PromptSectionName(execution.PromptTitle, execution.SuggestedName);
            if (promptedName is null)
                return;
        }

        SlideSectionPlanner.TryApplyAction(_editor, execution, promptedName);
    }

    private string? PromptSectionName(string title, string initialName)
    {
        var textBox = new TextBox
        {
            Text = initialName,
            MinWidth = 260,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var ok = new Button
        {
            Content = "OK",
            Width = 76,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Width = 76,
            IsCancel = true,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Section name:",
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(textBox);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Window.GetWindow(this),
        };

        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

        return dialog.ShowDialog() == true ? textBox.Text : null;
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
            if (Math.Abs(pos.Y - _dragStartPoint.Y) < SlidePanePlanner.DefaultDragStartThreshold) return;

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

            var action = SlidePanePlanner.PlanMoveAction(
                _editor.Presentation.Slides.Count,
                _dragSourceIndex,
                _dragTargetIndex);
            SlidePanePlanner.TryApplyAction(_editor, action);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Returns insertion index (0 = before slide 0, N = after last slide)
    /// based on Y coordinate relative to the StackPanel.
    /// Iterates over _stack children skipping section-header borders.
    /// </summary>
    private int HitTestInsertionPoint(double y)
    {
        return SlidePanePlanner.HitTestInsertionPoint(GetPaneItemKinds(), y, ItemHeight);
    }

    private void ShowInsertIndicator()
    {
        var plan = SlidePanePlanner.BuildDropVisualPlan(
            GetPaneItemKinds(),
            _dragSourceIndex,
            _dragTargetIndex,
            ItemHeight);

        if (!plan.IsVisible)
        {
            HideInsertIndicator();
            return;
        }

        _insertIndicator.Height     = plan.IndicatorThickness;
        _insertIndicator.Background = BrushFromHex(plan.AccentColorHex);
        _insertIndicator.Margin     = new Thickness(
            plan.HorizontalInset,
            plan.IndicatorTopMargin,
            plan.HorizontalInset,
            0);
        _insertIndicator.Visibility = Visibility.Visible;
    }

    private IReadOnlyList<bool> GetPaneItemKinds()
    {
        var result = new List<bool>(_stack.Children.Count);
        foreach (UIElement child in _stack.Children)
            result.Add(child is Border b && b.Tag is int);

        return result;
    }

    private void HideInsertIndicator() =>
        _insertIndicator.Visibility = Visibility.Collapsed;

    // ── Static helpers ────────────────────────────────────────────────────────────

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }

    private static Brush BrushFromHex(string hex) =>
        Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!));
}
