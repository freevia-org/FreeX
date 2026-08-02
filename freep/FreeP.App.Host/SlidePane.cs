using System.Windows;
using System.Windows.Automation;
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

    private const double ItemHeight = SlidePanePlanner.DefaultSlideItemHeight;

    // ── Fields ────────────────────────────────────────────────────────────────────

    private readonly EditingSession _editor;
    private readonly ScrollViewer   _scroll;
    private readonly StackPanel     _stack;
    private SlidePaneSessionState _sessionState = SlidePaneSessionState.Empty;
    private SlidePaneSessionProjection? _sessionProjection;

    // Insertion indicator: a thin horizontal line drawn over the scroll area.
    private readonly Border _insertIndicator;

    // Drag state
    private sealed record SectionHeaderTag(string SectionId, int SectionIndex);

    // ── Construction ──────────────────────────────────────────────────────────────

    public SlidePane(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));

        Background = BackgroundBrush;
        PresentationPaneAccessibilityAdapter.ApplyPaneMetadata(
            this,
            PresentationPaneAccessibilityPlanner.SlidePaneId,
            isVisible: true);

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

    private void OnCurrentSlideChanged(object? sender, EventArgs e)
    {
        _sessionState = SlidePanePlanner.SetSelectedSlide(_sessionState, _editor.CurrentSlideIndex);
        UpdateHighlight();
    }

    // ── List build ────────────────────────────────────────────────────────────────

    /// <summary>Fully rebuilds the item list. Called on structural changes.</summary>
    private void RebuildList()
    {
        _stack.Children.Clear();

        var slides = _editor.Presentation.Slides;
        _sessionState = SlidePanePlanner.SetSelectedSlide(_sessionState, _editor.CurrentSlideIndex);
        _sessionProjection = SlidePanePlanner.BuildSessionProjection(
            slides,
            _editor.Presentation.Sections,
            _sessionState);

        var accessibilityOrdinal = 0;
        foreach (var entry in _sessionProjection.Entries)
        {
            if (entry.Kind == SlidePaneEntryKind.SectionHeader)
            {
                _stack.Children.Add(BuildSectionHeader(entry, accessibilityOrdinal++));
                continue;
            }

            var plan = SlidePanePlanner.BuildThumbnailVisualPlan(
                entry,
                slides[entry.SlideIndex],
                _sessionProjection.SelectedSlideIndex);
            var item = BuildSlideItem(plan, slides[entry.SlideIndex], accessibilityOrdinal++);
            _stack.Children.Add(item);
        }

        // "New Slide" affordance at the bottom.
        _stack.Children.Add(BuildNewSlideButton());
    }

    /// <summary>
    /// Builds an interactive section-header row showing the section name and slide count.
    /// Wave 11B.
    /// </summary>
    private Border BuildSectionHeader(SlidePaneEntry entry, int accessibilityOrdinal)
    {
        var plan = SlidePanePlanner.BuildSectionHeaderVisualPlan(entry);
        var normalBackground = BrushFromHex(plan.BackgroundHex);
        var hoverBackground = BrushFromHex(plan.HoverBackgroundHex);

        var disclosure = new TextBlock
        {
            Text              = plan.DisclosureText,
            FontSize          = plan.FontSize,
            FontWeight        = FontWeights.Bold,
            Foreground        = BrushFromHex(plan.ForegroundHex),
            Width             = plan.DisclosureWidth,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var label = new TextBlock
        {
            Text                = plan.LabelText,
            FontSize            = plan.FontSize,
            FontWeight          = FontWeights.SemiBold,
            Foreground          = BrushFromHex(plan.ForegroundHex),
            VerticalAlignment   = VerticalAlignment.Center,
            TextTrimming        = TextTrimming.CharacterEllipsis,
        };

        var panel = new DockPanel();
        DockPanel.SetDock(disclosure, Dock.Left);
        panel.Children.Add(disclosure);
        panel.Children.Add(label);

        var header = new Border
        {
            Background      = normalBackground,
            Padding         = new Thickness(plan.HorizontalPadding, plan.VerticalPadding, plan.HorizontalPadding, plan.VerticalPadding),
            Margin          = new Thickness(0, plan.TopMargin, 0, plan.BottomMargin),
            MinHeight       = plan.HeaderHeight,
            CornerRadius    = new CornerRadius(plan.CornerRadius),
            Tag             = new SectionHeaderTag(plan.SectionId, plan.SectionIndex),
            ContextMenu     = BuildSectionContextMenu(entry),
            Child           = panel,
            Cursor          = Cursors.Hand,
            Focusable       = true,
            ToolTip         = plan.ToolTipText,
        };
        header.MouseEnter += (_, _) => header.Background = hoverBackground;
        header.MouseLeave += (_, _) => header.Background = normalBackground;
        header.MouseLeftButtonDown += (_, e) =>
        {
            ToggleSection(plan.SectionId);
            e.Handled = true;
        };
        header.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                ToggleSection(plan.SectionId);
                e.Handled = true;
            }
        };
        AutomationProperties.SetName(header, plan.AccessibleName);
        PresentationPaneAccessibilityAdapter.ApplyItem(
            header,
            PresentationPaneAccessibilityPlanner.SlidePaneId,
            accessibilityOrdinal,
            plan.AccessibleName,
            "Not selected",
            $"Section{plan.SectionIndex + 1}");

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

            if (_sessionProjection?.Entries.FirstOrDefault(entry =>
                    entry.Kind == SlidePaneEntryKind.Slide && entry.SlideIndex == itemIdx) is { } entry)
            {
                var plan = SlidePanePlanner.BuildThumbnailVisualPlan(
                    entry,
                    _editor.Presentation.Slides[itemIdx],
                    idx);
                AutomationProperties.SetName(item, plan.AccessibleName);
                PresentationPaneAccessibilityAdapter.ApplyItem(
                    item,
                    PresentationPaneAccessibilityPlanner.SlidePaneId,
                    GetAccessibilityOrdinalForSlide(itemIdx),
                    plan.AccessibleName,
                    selected ? "Selected" : "Not selected",
                    $"Slide{itemIdx + 1}");
            }
        }
    }

    private int GetAccessibilityOrdinalForSlide(int slideIndex)
    {
        if (_sessionProjection is null)
            return slideIndex;

        for (var ordinal = 0; ordinal < _sessionProjection.Entries.Count; ordinal++)
        {
            var entry = _sessionProjection.Entries[ordinal];
            if (entry.Kind == SlidePaneEntryKind.Slide && entry.SlideIndex == slideIndex)
                return ordinal;
        }

        return slideIndex;
    }

    // ── Item construction ─────────────────────────────────────────────────────────

    private Border BuildSlideItem(
        SlidePaneThumbnailVisualPlan plan,
        Slide slide,
        int accessibilityOrdinal)
    {
        // Slide-number label.
        var label = new TextBlock
        {
            Text                = plan.LabelText,
            FontSize            = plan.LabelFontSize,
            Foreground          = LabelBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Height              = plan.LabelHeight,
            VerticalAlignment   = VerticalAlignment.Center,
            Margin              = new Thickness(0, 0, 0, plan.LabelBottomMargin)
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
            BorderThickness = new Thickness(plan.ThumbnailBorderThickness),
            Child           = thumb
        };

        var panel = new StackPanel
        {
            Orientation         = Orientation.Vertical,
            HorizontalAlignment = plan.CenterThumbnailContent
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Stretch
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
            Margin          = new Thickness(
                plan.ItemMarginHorizontal,
                plan.ItemMarginVertical,
                plan.ItemMarginHorizontal,
                plan.ItemMarginVertical),
            Padding         = new Thickness(plan.ItemPadding),
            Child           = panel,
            Cursor          = Cursors.Hand,
            Focusable       = true,
            ToolTip         = plan.ToolTipText
        };
        AutomationProperties.SetName(item, plan.AccessibleName);
        PresentationPaneAccessibilityAdapter.ApplyItem(
            item,
            PresentationPaneAccessibilityPlanner.SlidePaneId,
            accessibilityOrdinal,
            plan.AccessibleName,
            plan.IsSelected ? "Selected" : "Not selected",
            $"Slide{plan.SlideIndex + 1}");

        // Click -> SelectSlide.
        item.MouseLeftButtonDown += (sender, e) =>
        {
            if (sender is Border b && b.Tag is int idx)
            {
                _sessionState = _sessionState with { DragSession = SlidePanePlanner.BeginDragSession(idx, e.GetPosition(b).Y) };
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
        item.LostMouseCapture  += OnItemLostMouseCapture;
        item.KeyDown           += OnSlideItemKeyDown;

        // Context menu.
        item.ContextMenu = BuildContextMenu(plan.SlideIndex);

        return item;
    }

    private Button BuildNewSlideButton()
    {
        var plan = SlidePanePlanner.BuildBottomNewSlideAffordance(
            _editor.Presentation.Slides.Count,
            _editor.CurrentSlideIndex);
        var btn = new Button
        {
            Content             = plan.Text,
            Margin              = new Thickness(12, 8, 12, 12),
            Padding             = new Thickness(0, 6, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background          = Freeze(new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A))),
            Foreground          = Brushes.White,
            BorderThickness     = new Thickness(0),
            FontSize            = 12,
            Cursor              = Cursors.Hand,
            Visibility          = plan.IsVisible ? Visibility.Visible : Visibility.Collapsed,
            IsEnabled           = plan.Action.IsEnabled,
            ToolTip             = plan.ToolTipText,
        };
        AutomationProperties.SetName(btn, plan.AccessibleName);
        btn.Click += (_, _) => SlidePanePlanner.TryApplyBottomNewSlideAffordance(_editor);
        return btn;
    }

    private ContextMenu BuildContextMenu(int index)
    {
        var menu = new ContextMenu();

        AddContextMenuEntries(
            menu,
            FreePContextMenuCatalog.BuildSlideMenu(
                _editor.Presentation.Slides,
                _editor.Presentation.Sections,
                index),
            command => ApplyContextMenuCommand(command, index, sectionIndex: -1));

        return menu;
    }

    private ContextMenu BuildSectionContextMenu(SlidePaneEntry entry)
    {
        var menu = new ContextMenu();

        AddContextMenuEntries(
            menu,
            FreePContextMenuCatalog.BuildSectionHeaderMenu(
                _editor.Presentation.Sections,
                entry.SectionIndex,
                entry.SlideIndex),
            command => ApplyContextMenuCommand(command, entry.SlideIndex, entry.SectionIndex));

        return menu;
    }

    internal ContextMenu BuildSlideContextMenuForTests(int slideIndex) =>
        BuildContextMenu(slideIndex);

    internal ContextMenu BuildSectionContextMenuForTests(SlidePaneEntry entry) =>
        BuildSectionContextMenu(entry);

    private static void AddContextMenuEntries(
        ContextMenu menu,
        IReadOnlyList<FreePContextMenuEntryPlan> entries,
        Action<FreePContextMenuCommand> execute)
    {
        foreach (var entry in entries)
        {
            if (entry.Kind == FreePContextMenuEntryKind.Separator)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var item = new MenuItem
            {
                Header = entry.Text,
                IsEnabled = entry.IsEnabled,
                IsCheckable = entry.IsCheckable,
                IsChecked = entry.IsChecked,
                Tag = entry.Command,
            };
            item.Click += (_, _) => execute(entry.Command!.Value);
            menu.Items.Add(item);
        }
    }

    private void ApplyContextMenuCommand(
        FreePContextMenuCommand command,
        int slideIndex,
        int sectionIndex)
    {
        if (command is FreePContextMenuCommand.NewSlide or
            FreePContextMenuCommand.DuplicateSlide or
            FreePContextMenuCommand.DeleteSlide or
            FreePContextMenuCommand.ToggleHiddenSlide)
        {
            var kind = command switch
            {
                FreePContextMenuCommand.NewSlide => SlidePaneActionKind.InsertAfterSlide,
                FreePContextMenuCommand.DuplicateSlide => SlidePaneActionKind.DuplicateSlide,
                FreePContextMenuCommand.DeleteSlide => SlidePaneActionKind.DeleteSlide,
                _ => SlidePaneActionKind.ToggleHiddenSlide,
            };
            var action = kind == SlidePaneActionKind.ToggleHiddenSlide
                ? SlidePanePlanner.BuildHiddenSlideAction(_editor.Presentation.Slides, slideIndex)
                : SlidePanePlanner.BuildContextActions(_editor.Presentation.Slides.Count, slideIndex)
                    .Single(candidate => candidate.Kind == kind);
            SlidePanePlanner.TryApplyAction(_editor, action);
            return;
        }

        var sectionActionKind = command switch
        {
            FreePContextMenuCommand.AddSection => SlideSectionActionKind.AddSection,
            FreePContextMenuCommand.RenameSection => SlideSectionActionKind.RenameSection,
            FreePContextMenuCommand.RemoveSection => SlideSectionActionKind.RemoveSection,
            FreePContextMenuCommand.RemoveAllSections => SlideSectionActionKind.RemoveAllSections,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
        var actions = sectionActionKind == SlideSectionActionKind.AddSection
            ? SlideSectionPlanner.BuildSlideContextActions(
                _editor.Presentation.Slides,
                _editor.Presentation.Sections,
                slideIndex)
            : SlideSectionPlanner.BuildSectionHeaderActions(
                _editor.Presentation.Sections,
                sectionIndex,
                slideIndex);
        ApplySectionAction(actions.Single(candidate => candidate.Kind == sectionActionKind));
    }

    private void ToggleSection(string sectionId)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
            return;

        _sessionState = SlidePanePlanner.ToggleSection(_sessionState, sectionId);

        RebuildList();
    }

    internal int SlidePaneSlideItemCount => _stack.Children
        .OfType<Border>()
        .Count(child => child.Tag is int);

    internal int SlidePaneSectionHeaderCount => _stack.Children
        .OfType<Border>()
        .Count(child => child.Tag is SectionHeaderTag);

    internal IReadOnlyList<string?> SlidePaneThumbnailAutomationNamesForTests => _stack.Children
        .OfType<Border>()
        .Where(child => child.Tag is int)
        .Select(AutomationProperties.GetName)
        .ToArray();

    internal IReadOnlyList<string?> SlidePaneSectionHeaderAutomationNamesForTests => _stack.Children
        .OfType<Border>()
        .Where(child => child.Tag is SectionHeaderTag)
        .Select(AutomationProperties.GetName)
        .ToArray();

    internal IReadOnlyList<FrameworkElement> AccessibilityItemsForTests => _stack.Children
        .OfType<FrameworkElement>()
        .Where(item => AutomationProperties.GetAutomationId(item)
            .StartsWith("FreePSlidePaneItem", StringComparison.Ordinal))
        .ToArray();

    internal bool ToggleSectionForTests(int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= _editor.Presentation.Sections.Count)
            return false;

        ToggleSection(SlidePanePlanner.GetSectionIdentity(_editor.Presentation.Sections[sectionIndex], sectionIndex));
        return true;
    }

    internal bool TryApplySlideSectionActionForTests(
        SlideSectionActionKind kind,
        int slideIndex = -1,
        int sectionIndex = -1,
        string? promptedName = null)
    {
        var action = kind == SlideSectionActionKind.AddSection
            ? SlideSectionPlanner.BuildSlideContextActions(
                    _editor.Presentation.Slides,
                    _editor.Presentation.Sections,
                    slideIndex)
                .SingleOrDefault(candidate => candidate.Kind == kind)
            : SlideSectionPlanner.BuildSectionHeaderActions(
                    _editor.Presentation.Sections,
                    sectionIndex,
                    slideIndex)
                .SingleOrDefault(candidate => candidate.Kind == kind);

        if (action is null)
            return false;

        var execution = SlideSectionPlanner.BuildExecutionPlan(action);
        return SlideSectionPlanner.TryApplyAction(_editor, execution, promptedName);
    }

    internal bool TryApplySlidePaneKeyboardAction(SlidePaneKeyboardIntentKind intent)
    {
        var action = SlidePanePlanner.BuildKeyboardAction(
            _editor.Presentation.Slides.Count,
            _editor.CurrentSlideIndex,
            intent);

        return SlidePanePlanner.TryApplyAction(_editor, action);
    }

    private void OnSlideItemKeyDown(object sender, KeyEventArgs e)
    {
        if (!TryMapKeyboardIntent(e, out var intent))
            return;

        if (TryApplySlidePaneKeyboardAction(intent))
            e.Handled = true;
    }

    private static bool TryMapKeyboardIntent(KeyEventArgs e, out SlidePaneKeyboardIntentKind intent)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        intent = key switch
        {
            Key.Insert when modifiers == ModifierKeys.None =>
                SlidePaneKeyboardIntentKind.InsertAfterCurrentSlide,
            Key.Delete when modifiers == ModifierKeys.None =>
                SlidePaneKeyboardIntentKind.DeleteCurrentSlide,
            Key.D when modifiers == ModifierKeys.Control =>
                SlidePaneKeyboardIntentKind.DuplicateCurrentSlide,
            Key.Up when modifiers == ModifierKeys.Alt =>
                SlidePaneKeyboardIntentKind.MoveCurrentSlideEarlier,
            Key.Down when modifiers == ModifierKeys.Alt =>
                SlidePaneKeyboardIntentKind.MoveCurrentSlideLater,
            _ => default,
        };

        return intent != SlidePaneKeyboardIntentKind.None;
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
        if (sender is not Border item || !_sessionState.DragSession.IsTracking) return;

        var update = SlidePanePlanner.UpdateDragSession(
            _sessionState.DragSession,
            GetPaneItemKinds(),
            e.GetPosition(item).Y,
            e.GetPosition(_stack).Y,
            ItemHeight);
        _sessionState = _sessionState with { DragSession = update.State };
        if (!_sessionState.DragSession.IsDragging) return;

        if (update.ShouldCapturePointer)
            item.CaptureMouse();

        ShowInsertIndicator(update.DropVisualPlan);
        e.Handled = true;
    }

    private void OnItemMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border item) return;

        var completion = SlidePanePlanner.CompleteDragSession(
            _sessionState.DragSession,
            _editor.Presentation.Slides.Count);
        _sessionState = _sessionState with { DragSession = completion.State };

        if (completion.ShouldReleaseCapture)
        {
            item.ReleaseMouseCapture();
            HideInsertIndicator();
            SlidePanePlanner.TryApplyAction(_editor, completion.Action);
        }

        e.Handled = true;
    }

    private void OnItemLostMouseCapture(object sender, MouseEventArgs e)
    {
        _sessionState = _sessionState with
        {
            DragSession = SlidePanePlanner.CancelDragSession(_sessionState.DragSession)
        };
        HideInsertIndicator();
    }

    private void ShowInsertIndicator(SlidePaneDropVisualPlan plan)
    {
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
        return _sessionProjection?.PaneItemIsSlide ?? Array.Empty<bool>();
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
