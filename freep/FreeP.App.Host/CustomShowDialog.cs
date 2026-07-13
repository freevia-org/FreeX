using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed class CustomShowDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
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
        _customShowSlideList.AllowDrop = true;
        _customShowSlideList.PreviewMouseLeftButtonDown += OnCustomShowSlideListMouseLeftButtonDown;
        _customShowSlideList.PreviewMouseMove += OnCustomShowSlideListMouseMove;
        _customShowSlideList.DragOver += OnCustomShowSlideListDragOver;
        _customShowSlideList.Drop += OnCustomShowSlideListDrop;

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

    public int RenderedCustomShowCount => _showList.Items.Count;

    public int RenderedSlideOptionCount => _slideCheckBoxes.Count;

    public int RenderedCustomShowSlideCount => _customShowSlideList.Items.Count;

    public int SelectedCustomShowSlideIndex => _customShowSlideList.SelectedIndex;

    public string ValidationMessage => _validationText.Text;

    public void SelectCustomShowSlideForTests(int index) =>
        _customShowSlideList.SelectedIndex = index;

    public void MoveSelectedCustomShowSlideUpForTests() => OnMoveSelectedSlide(-1);

    public void MoveSelectedCustomShowSlideDownForTests() => OnMoveSelectedSlide(1);

    public SlideShowCustomShowDragReorderPlan DragReorderCustomShowSlideForTests(
        int sourceSlideIndex,
        int targetDropIndex) =>
        ApplyCustomShowSlideDragReorder(sourceSlideIndex, targetDropIndex);

    private UIElement BuildContent()
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(_showList, 0);
        Grid.SetColumn(_showList, 0);
        root.Children.Add(_showList);

        var editor = new Grid();
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(118) });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var namePanel = new StackPanel();
        namePanel.Children.Add(new TextBlock
        {
            Text = "Name",
            FontWeight = FontWeights.SemiBold,
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
        };
        moveButtons.Children.Add(_moveUpButton);
        moveButtons.Children.Add(_moveDownButton);
        DockPanel.SetDock(moveButtons, Dock.Right);
        orderHeader.Children.Add(moveButtons);
        orderHeader.Children.Add(new TextBlock
        {
            Text = "Custom show order",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        editor.Children.Add(Position(orderHeader, row: 1));

        var customShowSlideScroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _customShowSlideList,
        };
        Grid.SetRow(customShowSlideScroller, 2);
        editor.Children.Add(customShowSlideScroller);

        editor.Children.Add(Position(new TextBlock
        {
            Text = "Deck slides",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 4),
        }, row: 3));

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
        };
        buttons.Children.Add(MakeButton("Create", OnCreate, isDefault: true));
        buttons.Children.Add(_renameButton);
        buttons.Children.Add(_updateButton);
        buttons.Children.Add(_deleteButton);
        buttons.Children.Add(_startButton);
        buttons.Children.Add(MakeButton("Close", Close, isCancel: true));
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);

        return root;
    }

    private void Refresh(int selectCustomShowIndex, int selectedCustomShowSlideIndex = -1)
    {
        var plan = _host.BuildCustomShowAuthoringPlan();
        _availableSlides = plan.AvailableSlides;
        _showList.ItemsSource = plan.CustomShows
            .Select(show => new CustomShowListItem(
                show.Index,
                show.Name,
                show.SlideIds.Count,
                FormatShowListText(show)))
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
        _nameBox.Text = selected?.Name ?? string.Empty;

        var selectedSlideIds = selected is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : _host.BuildCustomShowAuthoringPlan()
                .CustomShows
                .FirstOrDefault(show => show.Index == selected.Index)
                ?.SlideIds
                .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        var selectedSummary = selected is null
            ? null
            : _host.BuildCustomShowAuthoringPlan()
                .CustomShows
                .FirstOrDefault(show => show.Index == selected.Index);

        foreach (var checkBox in _slideCheckBoxes)
        {
            checkBox.IsChecked = checkBox.Tag is string slideId && selectedSlideIds.Contains(slideId);
        }

        RebuildCustomShowSlides(selectedSummary);

        var hasSelection = selected is not null;
        _renameButton.IsEnabled = hasSelection;
        _updateButton.IsEnabled = hasSelection;
        _deleteButton.IsEnabled = hasSelection;
        _startButton.IsEnabled = selected?.SlideCount > 0;
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

    private void OnCustomShowSlideListMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is CustomShowSlideListItem slide)
        {
            _customShowSlideDragStartPoint = e.GetPosition(_customShowSlideList);
            _customShowSlideDragSourceIndex = slide.Index;
            return;
        }

        _customShowSlideDragStartPoint = null;
        _customShowSlideDragSourceIndex = -1;
    }

    private void OnCustomShowSlideListMouseMove(object sender, MouseEventArgs e)
    {
        if (_customShowSlideDragStartPoint is not { } dragStartPoint ||
            _customShowSlideDragSourceIndex < 0 ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(_customShowSlideList);
        if (Math.Abs(position.X - dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var sourceIndex = _customShowSlideDragSourceIndex;
        _customShowSlideDragStartPoint = null;
        _customShowSlideDragSourceIndex = -1;
        DragDrop.DoDragDrop(_customShowSlideList, sourceIndex, DragDropEffects.Move);
    }

    private void OnCustomShowSlideListDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(int))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCustomShowSlideListDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(int)))
        {
            return;
        }

        var sourceSlideIndex = (int)e.Data.GetData(typeof(int))!;
        var targetDropIndex = ResolveCustomShowSlideDropIndex(e);
        ApplyCustomShowSlideDragReorder(sourceSlideIndex, targetDropIndex);
        e.Handled = true;
    }

    private int ResolveCustomShowSlideDropIndex(DragEventArgs e)
    {
        var item = FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is CustomShowSlideListItem slide)
        {
            var position = e.GetPosition(item);
            return position.Y > item.ActualHeight / 2
                ? slide.Index + 1
                : slide.Index;
        }

        return _customShowSlideList.Items.Count;
    }

    private SlideShowCustomShowDragReorderPlan ApplyCustomShowSlideDragReorder(
        int sourceSlideIndex,
        int targetDropIndex)
    {
        if (SelectedShow is null)
        {
            SetValidation(SlideShowCustomShowPlanner.MissingCustomShowMessage);
            return new SlideShowCustomShowDragReorderPlan(
                IsValid: false,
                ShouldApplyMutation: false,
                SourceSlideIndex: sourceSlideIndex,
                SourceSlideId: string.Empty,
                TargetDropIndex: targetDropIndex,
                TargetSlideIndex: -1,
                SelectedSlideIndex: -1,
                SlideIds: Array.Empty<string>(),
                ErrorMessage: SlideShowCustomShowPlanner.MissingCustomShowMessage);
        }

        var slideItems = _customShowSlideList.Items
            .OfType<CustomShowSlideListItem>()
            .ToArray();
        var sourceSlideId = sourceSlideIndex >= 0 && sourceSlideIndex < slideItems.Length
            ? slideItems[sourceSlideIndex].SlideId
            : string.Empty;
        var plan = SlideShowCustomShowPlanner.BuildCustomShowSlideDragReorderPlan(
            slideItems.Select(item => item.SlideId).ToArray(),
            sourceSlideIndex,
            sourceSlideId,
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
            SelectedShow.Index,
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

    private void RebuildCustomShowSlides(SlideShowCustomShowSummary? show)
    {
        var titleBySlideId = _availableSlides.ToDictionary(
            slide => slide.SlideId,
            slide => $"Slide {slide.Index + 1}: {slide.Title}",
            StringComparer.Ordinal);
        _customShowSlideList.ItemsSource = show?.SlideIds
            .Select((slideId, index) => new CustomShowSlideListItem(
                index,
                slideId,
                titleBySlideId.TryGetValue(slideId, out var title)
                    ? title
                    : $"Missing slide: {slideId}"))
            .ToArray() ?? Array.Empty<CustomShowSlideListItem>();
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

    private static string FormatShowListText(SlideShowCustomShowSummary show)
    {
        var name = string.IsNullOrWhiteSpace(show.Name)
            ? $"Custom Show {show.Index + 1}"
            : show.Name;
        var slideLabel = show.SlideIds.Count == 1 ? "slide" : "slides";
        return $"{name} ({show.SlideIds.Count} {slideLabel})";
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
            MinWidth = 82,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(8, 3, 8, 3),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static UIElement Position(UIElement element, int row)
    {
        Grid.SetRow(element, row);
        return element;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
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
