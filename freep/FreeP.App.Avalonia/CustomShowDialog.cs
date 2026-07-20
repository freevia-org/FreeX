using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class CustomShowDialog : Window
{
    private const double DragStartThreshold = 4;

    private readonly MainWindow _host;
    private readonly ListBox _showList = new();
    private readonly TextBox _nameBox = new();
    private readonly ListBox _customShowSlideList = new();
    private readonly StackPanel _slidePanel = new();
    private readonly TextBlock _validationText = new();
    private readonly Button _renameButton;
    private readonly Button _updateButton;
    private readonly Button _deleteButton;
    private readonly Button _startButton;
    private readonly Button _moveUpButton;
    private readonly Button _moveDownButton;
    private readonly List<CheckBox> _slideCheckBoxes = new();
    private IReadOnlyList<SlideShowCustomShowSlideOption> _availableSlides = Array.Empty<SlideShowCustomShowSlideOption>();
    private Point? _customShowSlideDragStartPoint;
    private int _customShowSlideDragSourceIndex = -1;
    private bool _customShowSlideDragActive;

    public CustomShowDialog(MainWindow host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));

        Title = "Custom Shows";
        Width = 640;
        Height = 440;
        MinWidth = 560;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _showList.Margin = new Thickness(0, 0, 10, 0);
        _showList.SelectionChanged += (_, _) => ApplySelectedShowToFields();

        _nameBox.MinWidth = 260;
        _nameBox.Margin = new Thickness(0, 0, 0, 8);

        _customShowSlideList.MinHeight = 92;
        _customShowSlideList.SelectionChanged += (_, _) => UpdateMoveButtons();
        DragDrop.SetAllowDrop(_customShowSlideList, true);
        _customShowSlideList.PointerPressed += OnCustomShowSlideListPointerPressed;
        _customShowSlideList.PointerMoved += OnCustomShowSlideListPointerMoved;
        _customShowSlideList.PointerReleased += OnCustomShowSlideListPointerReleased;
        _customShowSlideList.AddHandler(DragDrop.DragOverEvent, OnCustomShowSlideListDragOver);
        _customShowSlideList.AddHandler(DragDrop.DropEvent, OnCustomShowSlideListDrop);

        _validationText.Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A));
        _validationText.TextWrapping = TextWrapping.Wrap;
        _validationText.Margin = new Thickness(0, 4, 0, 8);

        _renameButton = MakeButton("Rename", OnRename);
        _updateButton = MakeButton("Update Slides", OnUpdateSlides);
        _deleteButton = MakeButton("Delete", OnDelete);
        _startButton = MakeButton("Start Show", OnStartShow);
        _moveUpButton = MakeButton("Move Up", () => OnMoveSelectedSlide(-1));
        _moveDownButton = MakeButton("Move Down", () => OnMoveSelectedSlide(1));

        Content = BuildContent();
        Refresh(selectCustomShowIndex: 0);
    }

    internal int RenderedCustomShowCount => _showList.Items.Count;

    internal int RenderedSlideOptionCount => _slideCheckBoxes.Count;

    internal int RenderedCustomShowSlideCount => _customShowSlideList.Items.Count;

    internal int SelectedCustomShowSlideIndex => _customShowSlideList.SelectedIndex;

    internal string ValidationMessage => _validationText.Text ?? string.Empty;

    internal void SelectCustomShowSlideForTests(int index) =>
        _customShowSlideList.SelectedIndex = index;

    internal void MoveSelectedCustomShowSlideUpForTests() => OnMoveSelectedSlide(-1);

    internal void MoveSelectedCustomShowSlideDownForTests() => OnMoveSelectedSlide(1);

    internal SlideShowCustomShowDragReorderPlan DragReorderCustomShowSlideForTests(
        int sourceSlideIndex,
        int targetDropIndex) =>
        ApplyCustomShowSlideDragReorder(sourceSlideIndex, targetDropIndex);

    internal void PrepareValidationForVisualEvidence()
    {
        _nameBox.Text = string.Empty;
        OnCreate();
    }

    private Control BuildContent()
    {
        var root = new Grid
        {
            Margin = new Thickness(14),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(210) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
            },
        };

        Grid.SetRow(_showList, 0);
        Grid.SetColumn(_showList, 0);
        root.Children.Add(_showList);

        var editor = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(118) },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
            },
        };

        var namePanel = new StackPanel();
        namePanel.Children.Add(new TextBlock
        {
            Text = "Name",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        namePanel.Children.Add(_nameBox);
        Grid.SetRow(namePanel, 0);
        editor.Children.Add(namePanel);

        var orderHeader = new DockPanel { Margin = new Thickness(0, 2, 0, 4), LastChildFill = true };
        var moveButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Children =
            {
                _moveUpButton,
                _moveDownButton,
            },
        };
        DockPanel.SetDock(moveButtons, Dock.Right);
        orderHeader.Children.Add(moveButtons);
        orderHeader.Children.Add(new TextBlock
        {
            Text = "Custom show order",
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetRow(orderHeader, 1);
        editor.Children.Add(orderHeader);

        var customShowSlideScroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _customShowSlideList,
        };
        Grid.SetRow(customShowSlideScroller, 2);
        editor.Children.Add(customShowSlideScroller);

        var slidesHeader = new TextBlock
        {
            Text = "Deck slides",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 8, 0, 4),
        };
        Grid.SetRow(slidesHeader, 3);
        editor.Children.Add(slidesHeader);

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _slidePanel,
        };
        Grid.SetRow(scroller, 4);
        editor.Children.Add(scroller);

        Grid.SetRow(_validationText, 5);
        editor.Children.Add(_validationText);

        Grid.SetRow(editor, 0);
        Grid.SetColumn(editor, 1);
        root.Children.Add(editor);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Spacing = 6,
            Children =
            {
                MakeButton("Create", OnCreate, isDefault: true),
                _renameButton,
                _updateButton,
                _deleteButton,
                _startButton,
                MakeButton("Close", Close, isCancel: true),
            },
        };
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);

        return root;
    }

    private void Refresh(int selectCustomShowIndex, int selectedCustomShowSlideIndex = -1)
    {
        var plan = _host.BuildCustomShowSessionPlan(
            new SlideShowCustomShowSessionState(selectCustomShowIndex, selectedCustomShowSlideIndex));
        _availableSlides = plan.AvailableSlides;
        _showList.ItemsSource = plan.CustomShows
            .Select(show => new CustomShowListItem(
                show.Index,
                show.Name,
                show.SlideCount,
                show.DisplayText))
            .ToArray();

        RebuildSlides(plan.AvailableSlides);

        var selected = _showList.Items
            .OfType<CustomShowListItem>()
            .FirstOrDefault(item => item.Index == selectCustomShowIndex);
        _showList.SelectedItem = selected ?? _showList.Items.OfType<CustomShowListItem>().FirstOrDefault();
        ApplySelectedShowToFields();
        if (selectedCustomShowSlideIndex >= 0 && _customShowSlideList.Items.Count > 0)
        {
            _customShowSlideList.SelectedIndex = Math.Clamp(
                selectedCustomShowSlideIndex,
                0,
                _customShowSlideList.Items.Count - 1);
        }
    }

    private void RebuildSlides(IReadOnlyList<SlideShowCustomShowSlideOption> slides)
    {
        _slidePanel.Children.Clear();
        _slideCheckBoxes.Clear();

        foreach (var slide in slides)
        {
            var checkBox = new CheckBox
            {
                Content = $"Slide {slide.Index + 1}: {slide.Title}",
                Tag = slide.SlideId,
                Margin = new Thickness(0, 2, 0, 2),
            };
            _slideCheckBoxes.Add(checkBox);
            _slidePanel.Children.Add(checkBox);
        }
    }

    private void ApplySelectedShowToFields()
    {
        var selected = SelectedShow;
        var session = _host.BuildCustomShowSessionPlan(
            selected is null
                ? new SlideShowCustomShowSessionState(-1)
                : SlideShowCustomShowSessionPlanner.SelectShow(selected.Index));
        _nameBox.Text = session.SelectedShow?.Name ?? string.Empty;

        foreach (var checkBox in _slideCheckBoxes)
        {
            checkBox.IsChecked = checkBox.Tag is string slideId && session.SelectedSlideIds.Contains(slideId);
        }

        RebuildCustomShowSlides(session.SelectedSlides);
        _renameButton.IsEnabled = session.CanRename;
        _updateButton.IsEnabled = session.CanUpdateSlides;
        _deleteButton.IsEnabled = session.CanDelete;
        _startButton.IsEnabled = session.CanStart;
        UpdateMoveButtons();
        SetValidation(null);
    }

    private void OnCreate()
    {
        var result = _host.CreateCustomShow(_nameBox.Text, SelectedSlideIds());
        ApplyMutationResult(result);
    }

    private void OnRename()
    {
        if (SelectedShow is null)
        {
            SetValidation(SlideShowCustomShowPlanner.MissingCustomShowMessage);
            return;
        }

        ApplyMutationResult(_host.RenameCustomShow(SelectedShow.Index, _nameBox.Text));
    }

    private void OnUpdateSlides()
    {
        if (SelectedShow is null)
        {
            SetValidation(SlideShowCustomShowPlanner.MissingCustomShowMessage);
            return;
        }

        ApplyMutationResult(_host.UpdateCustomShowSlides(SelectedShow.Index, SelectedSlideIds()));
    }

    private void OnMoveSelectedSlide(int offset)
    {
        if (SelectedShow is null)
        {
            SetValidation(SlideShowCustomShowPlanner.MissingCustomShowMessage);
            return;
        }

        if (_customShowSlideList.SelectedItem is not CustomShowSlideListItem selectedSlide)
        {
            SetValidation(SlideShowCustomShowPlanner.MissingCustomShowSlideMessage);
            return;
        }

        var targetDropIndex = selectedSlide.Index + offset + (offset > 0 ? 1 : 0);
        ApplyCustomShowSlideDragReorder(selectedSlide.Index, targetDropIndex);
    }

    private void OnCustomShowSlideListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_customShowSlideList);
        var item = FindControlAncestor<ListBoxItem>(e.Source);
        if (point.Properties.IsLeftButtonPressed &&
            item?.DataContext is CustomShowSlideListItem slide)
        {
            _customShowSlideDragStartPoint = point.Position;
            _customShowSlideDragSourceIndex = slide.Index;
            _customShowSlideDragActive = false;
            return;
        }

        ResetCustomShowSlideDrag();
    }

    private void OnCustomShowSlideListPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(_customShowSlideList);
        if (_customShowSlideDragStartPoint is not { } dragStartPoint ||
            _customShowSlideDragSourceIndex < 0 ||
            !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (Math.Abs(point.Position.X - dragStartPoint.X) < DragStartThreshold &&
            Math.Abs(point.Position.Y - dragStartPoint.Y) < DragStartThreshold)
        {
            return;
        }

        _customShowSlideDragActive = true;
        e.Handled = true;
    }

    private void OnCustomShowSlideListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_customShowSlideDragActive || _customShowSlideDragSourceIndex < 0)
        {
            ResetCustomShowSlideDrag();
            return;
        }

        var sourceSlideIndex = _customShowSlideDragSourceIndex;
        var targetDropIndex = ResolveCustomShowSlideDropIndex(e);
        ResetCustomShowSlideDrag();
        ApplyCustomShowSlideDragReorder(sourceSlideIndex, targetDropIndex);
        e.Handled = true;
    }

    private void OnCustomShowSlideListDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = _customShowSlideDragSourceIndex >= 0
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCustomShowSlideListDrop(object? sender, DragEventArgs e)
    {
        if (_customShowSlideDragSourceIndex < 0)
        {
            return;
        }

        var sourceSlideIndex = _customShowSlideDragSourceIndex;
        var targetDropIndex = ResolveCustomShowSlideDropIndex(e);
        ResetCustomShowSlideDrag();
        ApplyCustomShowSlideDragReorder(sourceSlideIndex, targetDropIndex);
        e.Handled = true;
    }

    private int ResolveCustomShowSlideDropIndex(PointerEventArgs e)
    {
        var item = FindControlAncestor<ListBoxItem>(e.Source);
        if (item?.DataContext is CustomShowSlideListItem slide)
        {
            var position = e.GetPosition(item);
            return position.Y > item.Bounds.Height / 2
                ? slide.Index + 1
                : slide.Index;
        }

        return _customShowSlideList.Items.Count;
    }

    private int ResolveCustomShowSlideDropIndex(DragEventArgs e)
    {
        var item = FindControlAncestor<ListBoxItem>(e.Source);
        if (item?.DataContext is CustomShowSlideListItem slide)
        {
            var position = e.GetPosition(item);
            return position.Y > item.Bounds.Height / 2
                ? slide.Index + 1
                : slide.Index;
        }

        return _customShowSlideList.Items.Count;
    }

    private void ResetCustomShowSlideDrag()
    {
        _customShowSlideDragStartPoint = null;
        _customShowSlideDragSourceIndex = -1;
        _customShowSlideDragActive = false;
    }

    private SlideShowCustomShowDragReorderPlan ApplyCustomShowSlideDragReorder(
        int sourceSlideIndex,
        int targetDropIndex)
    {
        var selected = SelectedShow;
        var session = _host.BuildCustomShowSessionPlan(
            selected is null
                ? new SlideShowCustomShowSessionState(-1)
                : SlideShowCustomShowSessionPlanner.SelectShow(selected.Index));
        var plan = SlideShowCustomShowSessionPlanner.BuildDragReorderPlan(
            session,
            sourceSlideIndex,
            targetDropIndex);

        if (!plan.IsValid)
        {
            SetValidation(plan.ErrorMessage);
            return plan;
        }

        if (!plan.ShouldApplyMutation)
        {
            _customShowSlideList.SelectedIndex = plan.SelectedSlideIndex;
            SetValidation(null);
            return plan;
        }

        ApplyMutationResult(_host.MoveCustomShowSlide(
            selected!.Index,
            plan.SourceSlideIndex,
            plan.SourceSlideId,
            plan.TargetSlideIndex));
        return plan;
    }

    private void OnDelete()
    {
        if (SelectedShow is null)
        {
            SetValidation(SlideShowCustomShowPlanner.MissingCustomShowMessage);
            return;
        }

        var deletedIndex = SelectedShow.Index;
        var result = _host.DeleteCustomShow(deletedIndex);
        if (!result.Succeeded)
        {
            SetValidation(result.ErrorMessage);
            return;
        }

        Refresh(Math.Max(0, deletedIndex - 1));
    }

    private void OnStartShow()
    {
        if (SelectedShow is null)
        {
            SetValidation(SlideShowCustomShowPlanner.MissingCustomShowMessage);
            return;
        }

        if (!_host.TryStartCustomSlideShow(SelectedShow.Name))
        {
            SetValidation(SlideShowCustomShowPlanner.EmptyCustomShowMessage);
            return;
        }

        Close();
    }

    private void ApplyMutationResult(SlideShowCustomShowMutationResult result)
    {
        if (!result.Succeeded)
        {
            SetValidation(result.ErrorMessage);
            return;
        }

        Refresh(result.CustomShowIndex, result.SelectedSlideIndex);
    }

    private void RebuildCustomShowSlides(
        IReadOnlyList<SlideShowCustomShowSessionSlideItemPlan> slides)
    {
        _customShowSlideList.ItemsSource = slides
            .Select(slide => new CustomShowSlideListItem(
                slide.Index,
                slide.SlideId,
                slide.DisplayText))
            .ToArray();
        if (_customShowSlideList.Items.Count > 0 && _customShowSlideList.SelectedIndex < 0)
            _customShowSlideList.SelectedIndex = 0;
        UpdateMoveButtons();
    }

    private void UpdateMoveButtons()
    {
        var selectedIndex = _customShowSlideList.SelectedIndex;
        var hasCustomShowSlide = selectedIndex >= 0 && selectedIndex < _customShowSlideList.Items.Count;
        _moveUpButton.IsEnabled = hasCustomShowSlide && selectedIndex > 0;
        _moveDownButton.IsEnabled = hasCustomShowSlide && selectedIndex < _customShowSlideList.Items.Count - 1;
    }

    private IEnumerable<string?> SelectedSlideIds() =>
        _slideCheckBoxes
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => checkBox.Tag as string);

    private CustomShowListItem? SelectedShow => _showList.SelectedItem as CustomShowListItem;

    private void SetValidation(string? message) =>
        _validationText.Text = message ?? string.Empty;

    private static Button MakeButton(
        string label,
        Action onClick,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 82,
            Padding = new Thickness(8, 3),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static T? FindControlAncestor<T>(object? source)
        where T : Control
    {
        for (var current = source as Control; current is not null; current = current.Parent as Control)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private sealed record CustomShowListItem(int Index, string Name, int SlideCount, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    private sealed record CustomShowSlideListItem(int Index, string SlideId, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }
}
