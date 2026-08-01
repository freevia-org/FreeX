using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const double SlicerTimelinePaneWidth = 280;

    private static readonly IBrush SlicerTimelinePaneBackground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly IBrush SlicerTimelinePaneBorder = new SolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly IBrush SlicerTimelineCardBorder = new SolidColorBrush(Color.FromRgb(208, 208, 208));
    private static readonly IBrush SlicerTimelineActiveCardBorder = new SolidColorBrush(Color.FromRgb(91, 155, 213));
    private static readonly IBrush SlicerTimelineActiveCardBackground = new SolidColorBrush(Color.FromRgb(247, 251, 255));
    private static readonly IBrush SlicerTimelineSelectedTile = new SolidColorBrush(Color.FromRgb(91, 155, 213));
    private static readonly IBrush SlicerTimelineSelectedTileBorder = new SolidColorBrush(Color.FromRgb(65, 120, 184));
    private static readonly IBrush SlicerTimelineUnselectedTile = new SolidColorBrush(Color.FromRgb(242, 242, 242));
    private static readonly IBrush SlicerTimelineUnselectedTileBorder = new SolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly IBrush SlicerTimelineMutedText = new SolidColorBrush(Color.FromRgb(102, 102, 102));

    private readonly Border _slicerTimelinePaneHost = new();
    private readonly Button _slicerTimelinePaneCloseButton = new();
    private bool _slicerTimelinePaneDismissed;
    private int _slicerTimelinePaneBuildCount;

    internal Border SlicerTimelinePaneHostForTest => _slicerTimelinePaneHost;
    internal bool SlicerTimelinePaneVisibleForTest => _slicerTimelinePaneHost.IsVisible;
    internal int SlicerTimelinePaneBuildCountForTest => _slicerTimelinePaneBuildCount;
    internal void RefreshSlicerTimelinePaneForTest() => RefreshSlicerTimelinePane();

    private Control BuildSlicerTimelinePaneChrome()
    {
        _slicerTimelinePaneHost.Width = SlicerTimelinePaneWidth;
        _slicerTimelinePaneHost.Background = SlicerTimelinePaneBackground;
        _slicerTimelinePaneHost.BorderBrush = SlicerTimelinePaneBorder;
        _slicerTimelinePaneHost.BorderThickness = new Thickness(1, 0, 0, 0);
        _slicerTimelinePaneHost.Focusable = true;
        _slicerTimelinePaneHost.IsVisible = false;
        AutomationProperties.SetAutomationId(_slicerTimelinePaneHost, "SlicerTimelinePane");
        AutomationProperties.SetName(
            _slicerTimelinePaneHost,
            UiText.Get("MainWindow_AutomationName_SlicersAndTimelines"));

        _slicerTimelinePaneCloseButton.Content = "X";
        _slicerTimelinePaneCloseButton.Width = 22;
        _slicerTimelinePaneCloseButton.Height = 22;
        _slicerTimelinePaneCloseButton.Padding = new Thickness(0);
        _slicerTimelinePaneCloseButton.HorizontalAlignment = AvaloniaHorizontalAlignment.Right;
        AutomationProperties.SetAutomationId(_slicerTimelinePaneCloseButton, "SlicerTimelinePaneCloseButton");
        AutomationProperties.SetName(
            _slicerTimelinePaneCloseButton,
            UiText.Get("MainWindow_AutomationName_CloseSlicersAndTimelines"));
        _slicerTimelinePaneCloseButton.Click += (_, _) => CloseSlicerTimelinePane();

        return _slicerTimelinePaneHost;
    }

    private void RefreshSlicerTimelinePane()
    {
        var filters = SlicerTimelinePanePlanner.GetNativeVisualFilters(_session.Workbook, _session.ActiveSheet);
        if (filters.Slicers.Count == 0 && filters.Timelines.Count == 0)
        {
            _slicerTimelinePaneHost.IsVisible = false;
            _slicerTimelinePaneHost.Child = null;
            _slicerTimelinePaneDismissed = false;
            return;
        }

        if (_slicerTimelinePaneDismissed)
        {
            _slicerTimelinePaneHost.IsVisible = false;
            return;
        }

        _slicerTimelinePaneHost.Child = BuildSlicerTimelinePaneBody(filters);
        _slicerTimelinePaneHost.IsVisible = true;
    }

    private Control BuildSlicerTimelinePaneBody(NativeVisualFilters filters)
    {
        _slicerTimelinePaneBuildCount++;
        var content = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
        var header = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Margin = new Thickness(0, 0, 0, 4),
        };
        var title = new TextBlock
        {
            Text = UiText.Get("MainWindow_Text_SlicersAndTimelines"),
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        AvaloniaGrid.SetColumn(title, 0);
        AvaloniaGrid.SetColumn(_slicerTimelinePaneCloseButton, 1);
        header.Children.Add(title);
        header.Children.Add(_slicerTimelinePaneCloseButton);
        content.Children.Add(header);

        foreach (var slicer in filters.Slicers.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
            content.Children.Add(BuildSlicerPaneCard(slicer));
        foreach (var timeline in filters.Timelines.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
            content.Children.Add(BuildTimelinePaneCard(timeline));

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content,
        };
    }

    private Control BuildSlicerPaneCard(SlicerModel slicer)
    {
        var paneItem = new SlicerPaneItem(
            slicer.Name,
            slicer.SourceFieldName ?? slicer.CacheName,
            SlicerTimelinePanePlanner.BuildSlicerTiles(slicer, ReadSlicerSourceItems(slicer)),
            SlicerTimelinePanePlanner.HasActiveSlicerFilter(slicer));
        var body = new StackPanel { Spacing = 2 };
        body.Children.Add(new TextBlock { Text = paneItem.Name, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBlock
        {
            Text = paneItem.FieldName,
            FontSize = 11,
            Foreground = SlicerTimelineMutedText,
            Margin = new Thickness(0, 1, 0, 4),
        });

        foreach (var tile in paneItem.Tiles)
        {
            var tileButton = new Button
            {
                Content = tile.Caption,
                MinHeight = 24,
                Margin = new Thickness(0, 1),
                Padding = new Thickness(6, 2),
                HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
                Background = tile.IsSelected ? SlicerTimelineSelectedTile : SlicerTimelineUnselectedTile,
                BorderBrush = tile.IsSelected ? SlicerTimelineSelectedTileBorder : SlicerTimelineUnselectedTileBorder,
                Foreground = tile.IsSelected ? Brushes.White : SlicerTimelineMutedText,
                FontWeight = tile.IsSelected ? FontWeight.SemiBold : FontWeight.Normal,
            };
            AutomationProperties.SetAutomationId(tileButton, $"SlicerPaneTile_{slicer.Name}_{tile.Caption}");
            AutomationProperties.SetName(tileButton, tile.Caption);
            var pendingModifiers = KeyModifiers.None;
            tileButton.PointerPressed += (_, args) =>
            {
                if (args.GetCurrentPoint(tileButton).Properties.IsLeftButtonPressed)
                    pendingModifiers = args.KeyModifiers;
            };
            tileButton.Click += (_, _) =>
            {
                HandleSlicerPaneTileClick(slicer, tile.Caption, pendingModifiers);
                pendingModifiers = KeyModifiers.None;
            };
            body.Children.Add(tileButton);
        }

        var clearButton = new Button
        {
            Content = UiText.Get("MainWindow_Content_ClearFilter"),
            MinHeight = 24,
            Margin = new Thickness(0, 6, 0, 0),
            IsEnabled = paneItem.HasActiveFilter,
        };
        AutomationProperties.SetAutomationId(clearButton, $"SlicerPaneClear_{slicer.Name}");
        AutomationProperties.SetName(
            clearButton,
            UiText.Get("MainWindow_AutomationName_ClearSlicerFilter"));
        clearButton.Click += (_, _) =>
        {
            CommitFilterCommand(new SetSlicerSelectionCommand(slicer.Name, []), $"Slicer: {paneItem.Name}");
        };
        body.Children.Add(clearButton);

        return new Border
        {
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 2),
            Background = paneItem.HasActiveFilter ? SlicerTimelineActiveCardBackground : Brushes.White,
            BorderBrush = paneItem.HasActiveFilter ? SlicerTimelineActiveCardBorder : SlicerTimelineCardBorder,
            BorderThickness = new Thickness(1),
            Child = body,
        };
    }

    private Control BuildTimelinePaneCard(TimelineModel timeline)
    {
        var paneItem = SlicerTimelinePanePlanner.BuildTimelineItem(timeline);
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(new TextBlock { Text = paneItem.Name, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBlock
        {
            Text = paneItem.FieldName,
            FontSize = 11,
            Foreground = SlicerTimelineMutedText,
            Margin = new Thickness(0, 1, 0, 4),
        });

        var startBox = new TextBox { Text = paneItem.SelectedStartDate, Margin = new Thickness(0, 0, 3, 0) };
        var endBox = new TextBox { Text = paneItem.SelectedEndDate, Margin = new Thickness(3, 0, 0, 0) };
        AutomationProperties.SetAutomationId(startBox, $"TimelinePaneStart_{timeline.Name}");
        AutomationProperties.SetAutomationId(endBox, $"TimelinePaneEnd_{timeline.Name}");
        startBox.TextChanged += (_, _) => paneItem.SelectedStartDate = startBox.Text ?? string.Empty;
        endBox.TextChanged += (_, _) => paneItem.SelectedEndDate = endBox.Text ?? string.Empty;
        var dates = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        AvaloniaGrid.SetColumn(startBox, 0);
        AvaloniaGrid.SetColumn(endBox, 1);
        dates.Children.Add(startBox);
        dates.Children.Add(endBox);
        body.Children.Add(dates);

        var applyButton = new Button
        {
            Content = UiText.Get("MainWindow_Content_Apply"),
            MinHeight = 24,
            Margin = new Thickness(0, 2, 3, 0),
        };
        var clearButton = new Button
        {
            Content = UiText.Get("MainWindow_Content_Clear"),
            MinHeight = 24,
            Margin = new Thickness(3, 2, 0, 0),
            IsEnabled = paneItem.HasActiveFilter,
        };
        AutomationProperties.SetAutomationId(applyButton, $"TimelinePaneApply_{timeline.Name}");
        AutomationProperties.SetAutomationId(clearButton, $"TimelinePaneClear_{timeline.Name}");
        AutomationProperties.SetName(
            applyButton,
            UiText.Get("MainWindow_AutomationName_ApplyTimelineFilter"));
        AutomationProperties.SetName(
            clearButton,
            UiText.Get("MainWindow_AutomationName_ClearTimelineFilter"));
        applyButton.Click += (_, _) =>
        {
            CommitFilterCommand(
                new SetTimelineRangeCommand(
                    paneItem.Name,
                    SlicerTimelinePanePlanner.NormalizeTimelineDateInput(paneItem.SelectedStartDate),
                    SlicerTimelinePanePlanner.NormalizeTimelineDateInput(paneItem.SelectedEndDate)),
                $"Timeline: {paneItem.Name}");
        };
        clearButton.Click += (_, _) =>
        {
            CommitFilterCommand(new SetTimelineRangeCommand(paneItem.Name, null, null), $"Timeline: {paneItem.Name}");
        };
        var actions = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Margin = new Thickness(0, 2, 0, 0),
        };
        AvaloniaGrid.SetColumn(applyButton, 0);
        AvaloniaGrid.SetColumn(clearButton, 1);
        actions.Children.Add(applyButton);
        actions.Children.Add(clearButton);
        body.Children.Add(actions);

        return new Border
        {
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 2),
            Background = paneItem.HasActiveFilter ? SlicerTimelineActiveCardBackground : Brushes.White,
            BorderBrush = paneItem.HasActiveFilter ? SlicerTimelineActiveCardBorder : SlicerTimelineCardBorder,
            BorderThickness = new Thickness(1),
            Child = body,
        };
    }

    private void HandleSlicerPaneTileClick(SlicerModel slicer, string caption, KeyModifiers modifiers)
    {
        var allItems = ReadSlicerSourceItems(slicer).ToList();
        IReadOnlyList<string> selected = modifiers.HasFlag(KeyModifiers.Shift)
            ? SlicerTimelinePanePlanner.ExtendSlicerSelection(allItems, slicer.SelectedItems, caption)
            : modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta)
                ? SlicerTimelinePanePlanner.ToggleSlicerSelection(allItems, slicer.SelectedItems, caption)
                : SlicerTimelinePanePlanner.ReplaceSlicerSelection(slicer.SelectedItems, caption);

        CommitFilterCommand(new SetSlicerSelectionCommand(slicer.Name, selected), $"Slicer: {slicer.Name}");
    }

    private void CloseSlicerTimelinePane()
    {
        _slicerTimelinePaneDismissed = true;
        _slicerTimelinePaneHost.IsVisible = false;
        _slicerTimelinePaneHost.Child = null;
        FocusControl(_sheetGridHost);
    }

    private void ResetSlicerTimelinePaneState()
    {
        _slicerTimelinePaneDismissed = false;
        _slicerTimelinePaneHost.IsVisible = false;
        _slicerTimelinePaneHost.Child = null;
    }

    private bool IsSlicerTimelinePaneFocused() =>
        _slicerTimelinePaneHost.IsFocused ||
        _slicerTimelinePaneHost.GetVisualDescendants().OfType<Control>().Any(control => control.IsFocused);

    private bool FocusSlicerTimelinePane() =>
        _slicerTimelinePaneHost.IsVisible &&
        (FocusControl(_slicerTimelinePaneCloseButton) || FocusControl(_slicerTimelinePaneHost));

    private bool TryHandleSlicerTimelinePaneKey(KeyEventArgs args)
    {
        if (!_slicerTimelinePaneHost.IsVisible || args.Source is not Visual source ||
            !IsDescendantOf(source, _slicerTimelinePaneHost))
            return false;

        if (args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None)
        {
            CloseSlicerTimelinePane();
            args.Handled = true;
            return true;
        }

        if (args.Key != Key.Tab || args.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))
            return false;

        var focusables = new[] { _slicerTimelinePaneCloseButton }
            .Concat(_slicerTimelinePaneHost.GetVisualDescendants().OfType<Control>())
            .Where(control => control != _slicerTimelinePaneHost && control.Focusable && control.IsVisible && control.IsEnabled)
            .Distinct()
            .ToList();
        if (focusables.Count == 0)
            return false;

        var current = focusables.FindIndex(control => control.IsFocused);
        var delta = args.KeyModifiers == KeyModifiers.Shift ? -1 : 1;
        var next = current < 0 ? (delta > 0 ? 0 : focusables.Count - 1) : (current + delta + focusables.Count) % focusables.Count;
        focusables[next].Focus();
        args.Handled = true;
        return true;
    }
}
