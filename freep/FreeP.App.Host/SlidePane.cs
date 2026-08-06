using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;

namespace FreeP.App.Host;

/// <summary>
/// Native WPF realization of the renderer-neutral slide-pane session.
/// </summary>
public sealed class SlidePane : Border
{
    private readonly PresentationWorkareaSession _workarea;
    private readonly ListBox _list;
    private readonly Border _insertIndicator;
    private readonly Button _newSlideButton;
    private bool _realizing;
    private bool _restoreFocusAfterRefresh;

    private sealed record SectionHeaderTag(string SectionId, int SectionIndex);

    public SlidePane(PresentationWorkareaSession workarea)
    {
        _workarea = workarea ?? throw new ArgumentNullException(nameof(workarea));
        Background = BrushFromHex(SlidePanePlanner.DefaultPaneBackgroundHex);
        PresentationPaneAccessibilityAdapter.ApplyPaneMetadata(
            this,
            PresentationPaneAccessibilityPlanner.SlidePaneId,
            isVisible: true);

        _list = new ListBox
        {
            SelectionMode = SelectionMode.Extended,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        _list.SelectionChanged += OnNativeSelectionChanged;

        _insertIndicator = new Border
        {
            Height = SlidePanePlanner.DefaultDropIndicatorThickness,
            Background = BrushFromHex(SlidePanePlanner.DefaultDropIndicatorAccentHex),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _newSlideButton = new Button();
        var content = new DockPanel();
        DockPanel.SetDock(_newSlideButton, Dock.Bottom);
        content.Children.Add(_newSlideButton);
        content.Children.Add(_list);

        var overlay = new Grid();
        overlay.Children.Add(content);
        overlay.Children.Add(_insertIndicator);
        Child = overlay;

        RefreshProjection();
    }

    internal void RefreshProjection()
    {
        _restoreFocusAfterRefresh |= _list.IsKeyboardFocusWithin;
        var projection = _workarea.SlidePaneSession.Projection;
        _realizing = true;
        try
        {
            _list.Items.Clear();
            foreach (var item in projection.Items)
            {
                _list.Items.Add(item.SectionHeader is { } header
                    ? BuildSectionHeader(item, header)
                    : BuildSlideItem(item, item.Thumbnail!));
            }
            ApplyBottomAffordance(projection.BottomAffordance);
            SyncNativeSelection(scrollActiveIntoView: false);
        }
        finally
        {
            _realizing = false;
        }

        if (_restoreFocusAfterRefresh)
        {
            _restoreFocusAfterRefresh = false;
            GetActiveItem()?.Focus();
        }
    }

    internal void SyncNativeSelection(bool scrollActiveIntoView = true)
    {
        var selection = _workarea.SlidePaneSession.Selection;
        _realizing = true;
        try
        {
            foreach (var item in _list.Items.OfType<ListBoxItem>())
            {
                item.IsSelected = item.Tag is int slideIndex && selection.IsSelected(slideIndex);
            }
        }
        finally
        {
            _realizing = false;
        }

        if (scrollActiveIntoView && GetActiveItem() is { } active)
            _list.ScrollIntoView(active);
    }

    internal void RefreshItemChrome()
    {
        var projection = _workarea.SlidePaneSession.Projection;
        foreach (var item in _list.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is not int slideIndex || item.Content is not Border chrome)
                continue;

            var projected = projection.Items.FirstOrDefault(candidate =>
                candidate.Entry.Kind == SlidePaneEntryKind.Slide &&
                candidate.Entry.SlideIndex == slideIndex);
            if (projected?.Thumbnail is not { } plan)
                continue;

            ApplyThumbnailChrome(chrome, item, projected.AccessibilityOrdinal, plan);
        }
    }

    private ListBoxItem BuildSlideItem(
        PresentationSlidePaneItemProjection projected,
        SlidePaneThumbnailVisualPlan plan)
    {
        var slide = _workarea.Presentation.Slides[plan.SlideIndex];
        var label = new TextBlock
        {
            Text = plan.LabelText,
            FontSize = plan.LabelFontSize,
            Foreground = BrushFromHex(plan.LabelForegroundHex),
            HorizontalAlignment = HorizontalAlignment.Center,
            Height = plan.LabelHeight,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, plan.LabelBottomMargin),
        };
        var thumbnail = new SlideCanvas
        {
            Width = plan.ThumbnailWidth,
            Height = plan.ThumbnailHeight,
            Presentation = _workarea.Presentation,
            Slide = slide,
            IsHitTestVisible = false,
            IsEnabled = false,
        };
        var thumbnailBorder = new Border
        {
            BorderBrush = BrushFromHex(plan.ThumbnailBorderHex),
            BorderThickness = new Thickness(plan.ThumbnailBorderThickness),
            Child = thumbnail,
        };
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = plan.CenterThumbnailContent
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Stretch,
        };
        panel.Children.Add(label);
        panel.Children.Add(thumbnailBorder);

        var chrome = new Border
        {
            CornerRadius = new CornerRadius(plan.ItemCornerRadius),
            Padding = new Thickness(plan.ItemPadding),
            Child = panel,
        };
        var item = new ListBoxItem
        {
            Tag = plan.SlideIndex,
            Content = chrome,
            Padding = new Thickness(0),
            Margin = new Thickness(
                plan.ItemMarginHorizontal,
                plan.ItemMarginVertical,
                plan.ItemMarginHorizontal,
                plan.ItemMarginVertical),
            MinHeight = plan.ItemHeight,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = plan.ToolTipText,
            ContextMenu = BuildSlideContextMenu(plan.SlideIndex),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ApplyThumbnailChrome(chrome, item, projected.AccessibilityOrdinal, plan);

        item.MouseEnter += (_, _) =>
        {
            if (item.Tag is int index && !_workarea.SlidePaneSession.Selection.IsSelected(index))
                chrome.Background = BrushFromHex(plan.ItemHoverBackgroundHex);
        };
        item.MouseLeave += (_, _) => ApplyThumbnailChrome(
            chrome,
            item,
            projected.AccessibilityOrdinal,
            ResolveThumbnailPlan(plan.SlideIndex) ?? plan);
        item.PreviewMouseLeftButtonDown += OnItemMouseLeftButtonDown;
        item.MouseMove += OnItemMouseMove;
        item.MouseLeftButtonUp += OnItemMouseLeftButtonUp;
        item.LostMouseCapture += OnItemLostMouseCapture;
        item.KeyDown += OnSlideItemKeyDown;
        return item;
    }

    private ListBoxItem BuildSectionHeader(
        PresentationSlidePaneItemProjection projected,
        SlidePaneSectionHeaderVisualPlan plan)
    {
        var normalBackground = BrushFromHex(plan.BackgroundHex);
        var hoverBackground = BrushFromHex(plan.HoverBackgroundHex);
        var disclosure = new TextBlock
        {
            Text = plan.DisclosureText,
            FontSize = plan.FontSize,
            FontWeight = FontWeights.Bold,
            Foreground = BrushFromHex(plan.ForegroundHex),
            Width = plan.DisclosureWidth,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new TextBlock
        {
            Text = plan.LabelText,
            FontSize = plan.FontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFromHex(plan.ForegroundHex),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var row = new DockPanel();
        DockPanel.SetDock(disclosure, Dock.Left);
        row.Children.Add(disclosure);
        row.Children.Add(label);
        var chrome = new Border
        {
            Background = normalBackground,
            Padding = new Thickness(
                plan.HorizontalPadding,
                plan.VerticalPadding,
                plan.HorizontalPadding,
                plan.VerticalPadding),
            MinHeight = plan.HeaderHeight,
            CornerRadius = new CornerRadius(plan.CornerRadius),
            Child = row,
        };
        var item = new ListBoxItem
        {
            Content = chrome,
            Padding = new Thickness(0),
            Margin = new Thickness(0, plan.TopMargin, 0, plan.BottomMargin),
            MinHeight = plan.HeaderHeight,
            Tag = new SectionHeaderTag(plan.SectionId, plan.SectionIndex),
            ContextMenu = BuildSectionContextMenu(projected.Entry),
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = plan.ToolTipText,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        item.MouseEnter += (_, _) => chrome.Background = hoverBackground;
        item.MouseLeave += (_, _) => chrome.Background = normalBackground;
        item.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _workarea.ToggleSlidePaneSection(plan.SectionId);
            e.Handled = true;
        };
        item.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                _workarea.ToggleSlidePaneSection(plan.SectionId);
                e.Handled = true;
            }
        };
        AutomationProperties.SetName(item, plan.AccessibleName);
        PresentationPaneAccessibilityAdapter.ApplyItem(
            item,
            PresentationPaneAccessibilityPlanner.SlidePaneId,
            projected.AccessibilityOrdinal,
            plan.AccessibleName,
            "Not selected",
            $"Section{plan.SectionIndex + 1}");
        return item;
    }

    private void ApplyThumbnailChrome(
        Border chrome,
        ListBoxItem item,
        int accessibilityOrdinal,
        SlidePaneThumbnailVisualPlan plan)
    {
        chrome.Background = BrushFromHex(
            plan.IsSelected ? plan.ItemSelectedBackgroundHex : plan.ItemNormalBackgroundHex);
        chrome.BorderBrush = BrushFromHex(
            plan.IsSelected ? plan.ItemSelectedBorderHex : plan.ItemNormalBorderHex);
        chrome.BorderThickness = new Thickness(
            plan.IsSelected ? plan.SelectedBorderThickness : plan.NormalBorderThickness);
        AutomationProperties.SetName(item, plan.AccessibleName);
        PresentationPaneAccessibilityAdapter.ApplyItem(
            item,
            PresentationPaneAccessibilityPlanner.SlidePaneId,
            accessibilityOrdinal,
            plan.AccessibleName,
            plan.IsActive ? "Active and selected" : plan.IsSelected ? "Selected" : "Not selected",
            $"Slide{plan.SlideIndex + 1}");
    }

    private void ApplyBottomAffordance(SlidePaneBottomAffordancePlan plan)
    {
        _newSlideButton.Content = plan.Text;
        _newSlideButton.Margin = new Thickness(12, 8, 12, 12);
        _newSlideButton.Padding = new Thickness(0, 6, 0, 6);
        _newSlideButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _newSlideButton.Background = BrushFromHex(SlidePanePlanner.DefaultItemSelectedBorderHex);
        _newSlideButton.Foreground = Brushes.White;
        _newSlideButton.BorderThickness = new Thickness(0);
        _newSlideButton.FontSize = 12;
        _newSlideButton.Cursor = Cursors.Hand;
        _newSlideButton.Visibility = plan.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        _newSlideButton.IsEnabled = plan.Action.IsEnabled;
        _newSlideButton.ToolTip = plan.ToolTipText;
        AutomationProperties.SetName(_newSlideButton, plan.AccessibleName);
        _newSlideButton.Click -= OnNewSlideClick;
        _newSlideButton.Click += OnNewSlideClick;
    }

    private void OnNewSlideClick(object sender, RoutedEventArgs e) =>
        _workarea.ExecuteSlidePaneAction(
            SlidePaneActionKind.InsertAfterSlide,
            _workarea.SlidePaneSession.Selection.ActiveSlideIndex);

    private void OnNativeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_realizing)
            return;

        var selected = _list.SelectedItems
            .OfType<ListBoxItem>()
            .Where(item => item.Tag is int)
            .Select(item => (int)item.Tag)
            .ToArray();
        var active = e.AddedItems
            .OfType<ListBoxItem>()
            .Select(item => item.Tag)
            .OfType<int>()
            .LastOrDefault(_workarea.SlidePaneSession.Selection.ActiveSlideIndex);
        _workarea.ApplySlidePaneNativeSelection(selected, active);
    }

    private ContextMenu BuildSlideContextMenu(int slideIndex)
    {
        var menu = new ContextMenu();
        AddContextMenuEntries(
            menu,
            FreePContextMenuCatalog.BuildSlideMenu(
                _workarea.Presentation.Slides,
                _workarea.Presentation.Sections,
                slideIndex),
            command => ApplyContextCommand(command, slideIndex, sectionIndex: -1));
        return menu;
    }

    private ContextMenu BuildSectionContextMenu(SlidePaneEntry entry)
    {
        var menu = new ContextMenu();
        AddContextMenuEntries(
            menu,
            FreePContextMenuCatalog.BuildSectionHeaderMenu(
                _workarea.Presentation.Sections,
                entry.SectionIndex,
                entry.SlideIndex),
            command => ApplyContextCommand(command, entry.SlideIndex, entry.SectionIndex));
        return menu;
    }

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

    private void ApplyContextCommand(
        FreePContextMenuCommand command,
        int slideIndex,
        int sectionIndex)
    {
        var route = _workarea.BuildSlidePaneContextCommandRoute(command, slideIndex, sectionIndex);
        if (route.SlideAction is { } slideAction)
        {
            _workarea.ExecuteSlidePaneAction(slideAction.Kind, slideIndex, slideAction.TargetSlideIndex);
            return;
        }
        if (route.SectionExecution is { } sectionExecution)
            ApplySectionAction(sectionExecution);
    }

    private void ApplySectionAction(SlideSectionActionExecutionPlan execution)
    {
        if (!execution.IsEnabled)
            return;
        var name = execution.RequiresNamePrompt
            ? PromptSectionName(execution.PromptTitle, execution.SuggestedName)
            : null;
        if (execution.RequiresNamePrompt && name is null)
            return;
        _workarea.ExecuteSlidePaneSectionAction(execution, name);
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
        panel.Children.Add(new TextBlock { Text = "Section name:", Margin = new Thickness(0, 0, 0, 4) });
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

    private void OnSlideItemKeyDown(object sender, KeyEventArgs e)
    {
        if (!TryMapKeyboardIntent(e, out var intent))
            return;
        if (_workarea.ExecuteSlidePaneKeyboardAction(intent))
            e.Handled = true;
    }

    private static bool TryMapKeyboardIntent(KeyEventArgs e, out SlidePaneKeyboardIntentKind intent)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        intent = key switch
        {
            Key.Insert when modifiers == ModifierKeys.None => SlidePaneKeyboardIntentKind.InsertAfterCurrentSlide,
            Key.Delete when modifiers == ModifierKeys.None => SlidePaneKeyboardIntentKind.DeleteCurrentSlide,
            Key.D when modifiers == ModifierKeys.Control => SlidePaneKeyboardIntentKind.DuplicateCurrentSlide,
            Key.Up when modifiers == ModifierKeys.Alt => SlidePaneKeyboardIntentKind.MoveCurrentSlideEarlier,
            Key.Down when modifiers == ModifierKeys.Alt => SlidePaneKeyboardIntentKind.MoveCurrentSlideLater,
            _ => SlidePaneKeyboardIntentKind.None,
        };
        return intent != SlidePaneKeyboardIntentKind.None;
    }

    private void OnItemMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { Tag: int sourceSlideIndex } item)
            _workarea.BeginSlidePaneDrag(sourceSlideIndex, e.GetPosition(item).Y);
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not ListBoxItem item)
            return;
        var update = _workarea.UpdateSlidePaneDrag(
            e.GetPosition(item).Y,
            e.GetPosition(_list).Y);
        if (!update.State.IsDragging)
            return;
        if (update.ShouldCapturePointer)
            item.CaptureMouse();
        ShowInsertIndicator(update.DropVisualPlan);
        e.Handled = true;
    }

    private void OnItemMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem item)
            return;
        _workarea.CompleteSlidePaneDrag(out var shouldReleaseCapture);
        if (shouldReleaseCapture)
        {
            item.ReleaseMouseCapture();
            HideInsertIndicator();
        }
        e.Handled = true;
    }

    private void OnItemLostMouseCapture(object sender, MouseEventArgs e)
    {
        _workarea.CancelSlidePaneDrag();
        HideInsertIndicator();
    }

    private void ShowInsertIndicator(SlidePaneDropVisualPlan plan)
    {
        if (!plan.IsVisible)
        {
            HideInsertIndicator();
            return;
        }
        _insertIndicator.Height = plan.IndicatorThickness;
        _insertIndicator.Background = BrushFromHex(plan.AccentColorHex);
        _insertIndicator.Margin = new Thickness(
            plan.HorizontalInset,
            plan.IndicatorTopMargin,
            plan.HorizontalInset,
            0);
        _insertIndicator.Visibility = Visibility.Visible;
    }

    private void HideInsertIndicator() =>
        _insertIndicator.Visibility = Visibility.Collapsed;

    private ListBoxItem? GetActiveItem() => _list.Items
        .OfType<ListBoxItem>()
        .FirstOrDefault(item => item.Tag is int slideIndex &&
            slideIndex == _workarea.SlidePaneSession.Selection.ActiveSlideIndex);

    private SlidePaneThumbnailVisualPlan? ResolveThumbnailPlan(int slideIndex) =>
        _workarea.SlidePaneSession.Projection.Items
            .FirstOrDefault(item => item.Entry.Kind == SlidePaneEntryKind.Slide &&
                item.Entry.SlideIndex == slideIndex)
            ?.Thumbnail;

    internal ContextMenu BuildSlideContextMenuForTests(int slideIndex) =>
        BuildSlideContextMenu(slideIndex);

    internal ContextMenu BuildSectionContextMenuForTests(SlidePaneEntry entry) =>
        BuildSectionContextMenu(entry);

    internal int SlidePaneSlideItemCount => _list.Items
        .OfType<ListBoxItem>()
        .Count(item => item.Tag is int);

    internal int SlidePaneSectionHeaderCount => _list.Items
        .OfType<ListBoxItem>()
        .Count(item => item.Tag is SectionHeaderTag);

    internal IReadOnlyList<string?> SlidePaneThumbnailAutomationNamesForTests => _list.Items
        .OfType<ListBoxItem>()
        .Where(item => item.Tag is int)
        .Select(AutomationProperties.GetName)
        .ToArray();

    internal IReadOnlyList<string?> SlidePaneSectionHeaderAutomationNamesForTests => _list.Items
        .OfType<ListBoxItem>()
        .Where(item => item.Tag is SectionHeaderTag)
        .Select(AutomationProperties.GetName)
        .ToArray();

    internal IReadOnlyList<FrameworkElement> AccessibilityItemsForTests => _list.Items
        .OfType<FrameworkElement>()
        .Where(item => AutomationProperties.GetAutomationId(item)
            .StartsWith("FreePSlidePaneItem", StringComparison.Ordinal))
        .ToArray();

    internal bool ToggleSectionForTests(int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= _workarea.Presentation.Sections.Count)
            return false;
        _workarea.ToggleSlidePaneSection(SlidePanePlanner.GetSectionIdentity(
            _workarea.Presentation.Sections[sectionIndex],
            sectionIndex));
        return true;
    }

    internal bool TryApplySlideSectionActionForTests(
        SlideSectionActionKind kind,
        int slideIndex = -1,
        int sectionIndex = -1,
        string? promptedName = null)
    {
        var command = kind switch
        {
            SlideSectionActionKind.AddSection => FreePContextMenuCommand.AddSection,
            SlideSectionActionKind.RenameSection => FreePContextMenuCommand.RenameSection,
            SlideSectionActionKind.RemoveSection => FreePContextMenuCommand.RemoveSection,
            SlideSectionActionKind.RemoveAllSections => FreePContextMenuCommand.RemoveAllSections,
            _ => default,
        };
        var execution = _workarea.BuildSlidePaneContextCommandRoute(command, slideIndex, sectionIndex)
            .SectionExecution;
        return execution is not null && _workarea.ExecuteSlidePaneSectionAction(execution, promptedName);
    }

    internal bool TryApplySlidePaneKeyboardAction(SlidePaneKeyboardIntentKind intent) =>
        _workarea.ExecuteSlidePaneKeyboardAction(intent);

    internal ListBox NativeListForTests => _list;

    internal Button NewSlideButtonForTests => _newSlideButton;

    private static Brush BrushFromHex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }
}
