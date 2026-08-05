using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class CustomShowDialog : Window
{
    private const double DragStartThreshold = 4;
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly MainWindow _host;
    private readonly SlideShowCustomShowDialogSession _session;
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
    private readonly Button _removeButton;
    private readonly List<CheckBox> _slideCheckBoxes = new();
    private Point? _customShowSlideDragStartPoint;
    private int _customShowSlideDragSourceIndex = -1;
    private bool _customShowSlideDragActive;

    public CustomShowDialog(MainWindow host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _session = new SlideShowCustomShowDialogSession(
            new SlideShowCustomShowDialogSessionCallbacks(
                _host.BuildCustomShowSessionPlan,
                _host.ApplyCustomShowDialogMutation,
                name => _host.TryStartCustomSlideShow(name)));

        Title = "Custom Shows";
        Width = 625.3333333333334;
        Height = 402.6666666666667;
        MinWidth = 560;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _showList.Margin = new Thickness(0, 0, 10, 0);
        ApplyListChrome(_showList);
        _showList.SelectionChanged += (_, _) => OnSelectedShowChanged();

        _nameBox.MinWidth = 260;
        _nameBox.Margin = new Thickness(0, 0, 0, 8);
        AvaloniaCompactDialogChrome.ApplyTextBox(_nameBox, DialogChromeStyle);

        _customShowSlideList.MinHeight = 92;
        ApplyListChrome(_customShowSlideList);
        _customShowSlideList.SelectionChanged += (_, _) =>
            ApplyTransition(_session.SelectSlide(_customShowSlideList.SelectedIndex));
        DragDrop.SetAllowDrop(_customShowSlideList, true);
        _customShowSlideList.PointerPressed += OnCustomShowSlideListPointerPressed;
        _customShowSlideList.PointerMoved += OnCustomShowSlideListPointerMoved;
        _customShowSlideList.PointerReleased += OnCustomShowSlideListPointerReleased;
        _customShowSlideList.AddHandler(DragDrop.DragOverEvent, OnCustomShowSlideListDragOver);
        _customShowSlideList.AddHandler(DragDrop.DropEvent, OnCustomShowSlideListDrop);
        PointerCaptureLost += OnCustomShowSlidePointerCaptureLost;

        _validationText.Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A));
        _validationText.TextWrapping = TextWrapping.Wrap;
        _validationText.Margin = new Thickness(0, 4, 0, 8);

        _renameButton = MakeButton("Rename", OnRename);
        _updateButton = MakeButton("Update Slides", OnUpdateSlides);
        _deleteButton = MakeButton("Delete", OnDelete);
        _startButton = MakeButton("Start Show", OnStartShow);
        _moveUpButton = MakeButton("Move Up", () => OnMoveSelectedSlide(-1));
        _moveDownButton = MakeButton("Move Down", () => OnMoveSelectedSlide(1));
        _removeButton = MakeButton("Remove", OnRemoveSelectedSlide);

        Content = BuildContent();
        ApplyTransition(_session.InitialTransition);
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

    internal void RemoveSelectedCustomShowSlideForTests() => OnRemoveSelectedSlide();

    internal void AddCustomShowSlideOccurrenceForTests(string slideId) => AddSlideOccurrence(slideId);

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
                _removeButton,
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

    private void ApplyTransition(SlideShowCustomShowDialogSessionTransition transition)
    {
        switch (transition.RenderScope)
        {
            case SlideShowCustomShowDialogRenderScope.Full:
                RenderFullPlan(transition.Plan);
                break;
            case SlideShowCustomShowDialogRenderScope.SelectedShow:
                RenderSelectedShowPlan(transition.Plan);
                break;
            case SlideShowCustomShowDialogRenderScope.SlideSelection:
                ApplySlideSelection(transition.Plan);
                break;
        }

        SetValidation(transition.ValidationMessage);
        if (transition.ShouldClose)
            Close();
    }

    private void RenderFullPlan(SlideShowCustomShowSessionPlan plan)
    {
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
            .FirstOrDefault(item => item.Index == plan.SelectedShow?.Index);
        _showList.SelectedItem = selected ?? _showList.Items.OfType<CustomShowListItem>().FirstOrDefault();
        RenderSelectedShowPlan(plan);
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
            AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, DialogChromeStyle);
            checkBox.Height = 20;
            checkBox.MinHeight = 20;
            checkBox.MaxHeight = 20;
            checkBox.Padding = new Thickness(0);
            _slideCheckBoxes.Add(checkBox);
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), LastChildFill = true };
            var addButton = MakeButton("Add", () => AddSlideOccurrence(slide.SlideId));
            addButton.MinWidth = 58;
            DockPanel.SetDock(addButton, Dock.Right);
            row.Children.Add(addButton);
            row.Children.Add(checkBox);
            _slidePanel.Children.Add(row);
        }
    }

    private void OnSelectedShowChanged()
    {
        var selected = SelectedShow;
        ApplyTransition(_session.SelectShow(selected?.Index ?? -1));
    }

    private void RenderSelectedShowPlan(SlideShowCustomShowSessionPlan plan)
    {
        _nameBox.Text = plan.SelectedShow?.Name ?? string.Empty;

        foreach (var checkBox in _slideCheckBoxes)
        {
            checkBox.IsChecked = checkBox.Tag is string slideId && plan.SelectedSlideIds.Contains(slideId);
        }

        RebuildCustomShowSlides(plan.SelectedSlides);
        _renameButton.IsEnabled = plan.CanRename;
        _updateButton.IsEnabled = plan.CanUpdateSlides;
        _deleteButton.IsEnabled = plan.CanDelete;
        _startButton.IsEnabled = plan.CanStart;
        ApplySlideSelection(plan);
    }

    private void ApplySlideSelection(SlideShowCustomShowSessionPlan plan)
    {
        var selectedIndex = plan.SelectedSlideIndex >= 0 &&
            plan.SelectedSlideIndex < _customShowSlideList.Items.Count
                ? plan.SelectedSlideIndex
                : -1;
        if (_customShowSlideList.SelectedIndex != selectedIndex)
            _customShowSlideList.SelectedIndex = selectedIndex;

        _moveUpButton.IsEnabled = plan.CanMoveUp;
        _moveDownButton.IsEnabled = plan.CanMoveDown;
        _removeButton.IsEnabled = plan.CanRemove;
    }

    private void OnCreate() =>
        ApplyTransition(_session.Create(_nameBox.Text, SelectedSlideIds()));

    private void OnRename() =>
        ApplyTransition(_session.Rename(_nameBox.Text));

    private void OnUpdateSlides() =>
        ApplyTransition(_session.UpdateSlides(SelectedSlideIds()));

    private void AddSlideOccurrence(string slideId) =>
        ApplyTransition(_session.AddSlideOccurrence(slideId));

    private void OnRemoveSelectedSlide() =>
        ApplyTransition(_session.RemoveSelectedSlide());

    private void OnMoveSelectedSlide(int offset) =>
        ApplyTransition(_session.MoveSelectedSlide(offset));

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
        e.Pointer.Capture(this);
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
        var pointerPosition = e.GetPosition(_customShowSlideList);
        var isInsideList = new Rect(
            0,
            0,
            _customShowSlideList.Bounds.Width,
            _customShowSlideList.Bounds.Height).Contains(pointerPosition);
        var targetDropIndex = isInsideList ? ResolveCustomShowSlideDropIndex(e) : -1;
        e.Pointer.Capture(null);
        ResetCustomShowSlideDrag();
        CompleteCustomShowSlideDrag(sourceSlideIndex, targetDropIndex, isInsideList);
        e.Handled = true;
    }

    private void OnCustomShowSlidePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_customShowSlideDragActive || _customShowSlideDragSourceIndex >= 0)
            ResetCustomShowSlideDrag();
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
        var pointerPosition = e.GetPosition(_customShowSlideList);
        var item = FindControlAncestor<ListBoxItem>(
            _customShowSlideList.InputHitTest(pointerPosition) ?? e.Source);
        if (item?.DataContext is CustomShowSlideListItem slide)
        {
            var itemPosition = e.GetPosition(item);
            return itemPosition.Y > item.Bounds.Height / 2
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

    private bool CompleteCustomShowSlideDrag(
        int sourceSlideIndex,
        int targetDropIndex,
        bool isInsideList)
    {
        if (!isInsideList)
            return false;

        return ApplyCustomShowSlideDragReorder(sourceSlideIndex, targetDropIndex).ShouldApplyMutation;
    }

    internal bool CompleteCustomShowSlideDragForTests(
        int sourceSlideIndex,
        int targetDropIndex,
        bool isInsideList) =>
        CompleteCustomShowSlideDrag(sourceSlideIndex, targetDropIndex, isInsideList);

    internal bool IsCustomShowSlideDragActiveForTests => _customShowSlideDragActive;

    internal IPointer BeginCustomShowSlideDragForTests(int sourceSlideIndex)
    {
        _customShowSlideDragStartPoint = new Point();
        _customShowSlideDragSourceIndex = sourceSlideIndex;
        _customShowSlideDragActive = true;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        pointer.Capture(this);
        return pointer;
    }

    private SlideShowCustomShowDragReorderPlan ApplyCustomShowSlideDragReorder(
        int sourceSlideIndex,
        int targetDropIndex)
    {
        var transition = _session.Reorder(sourceSlideIndex, targetDropIndex);
        ApplyTransition(transition.SessionTransition);
        return transition.ReorderPlan;
    }

    private void OnDelete() =>
        ApplyTransition(_session.Delete());

    private void OnStartShow() =>
        ApplyTransition(_session.StartShow());

    private void RebuildCustomShowSlides(
        IReadOnlyList<SlideShowCustomShowSessionSlideItemPlan> slides)
    {
        _customShowSlideList.ItemsSource = slides
            .Select(slide => new CustomShowSlideListItem(
                slide.Index,
                slide.SlideId,
                slide.DisplayText))
            .ToArray();
    }

    private IEnumerable<string?> SelectedSlideIds() =>
        _slideCheckBoxes
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => checkBox.Tag as string);

    private CustomShowListItem? SelectedShow => _showList.SelectedItem as CustomShowListItem;

    private void SetValidation(string? message) =>
        _validationText.Text = message ?? string.Empty;

    private static void ApplyListChrome(ListBox listBox)
    {
        AvaloniaCompactDialogChrome.ApplyListBox(listBox, DialogChromeStyle);
        listBox.Background = Brushes.White;
        listBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
        listBox.BorderThickness = new Thickness(1);
    }

    private static Button MakeButton(
        string label,
        Action onClick,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new Button
        {
            Content = label,
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 82, isDefault: isDefault);
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
