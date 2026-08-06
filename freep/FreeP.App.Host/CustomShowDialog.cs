using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed class CustomShowDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
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

    private PresentationDialogSurfacePlan<
        SlideShowCustomShowDialogField,
        SlideShowCustomShowDialogAction> Surface => _session.Surface;

    public CustomShowDialog(MainWindow host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _session = new SlideShowCustomShowDialogSession(
            new SlideShowCustomShowDialogSessionCallbacks(
                _host.BuildCustomShowSessionPlan,
                _host.ApplyCustomShowDialogMutation,
                name => _host.TryStartCustomSlideShow(name)));

        Title = Surface.Title;
        AutomationProperties.SetName(this, Surface.AccessibleName);
        AutomationProperties.SetAutomationId(this, Surface.AutomationId);
        Width = 640;
        Height = 440;
        MinWidth = 560;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _showList.Margin = new Thickness(0, 0, 10, 0);
        ApplySemantic(_showList, Surface.Field(SlideShowCustomShowDialogField.CustomShows));
        _showList.SelectionChanged += (_, _) => OnSelectedShowChanged();

        _nameBox.MinWidth = 260;
        _nameBox.Margin = new Thickness(0, 0, 0, 8);
        ApplySemantic(_nameBox, Surface.Field(SlideShowCustomShowDialogField.Name));

        _customShowSlideList.MinHeight = 92;
        ApplySemantic(_customShowSlideList, Surface.Field(SlideShowCustomShowDialogField.OrderedSlides));
        _customShowSlideList.SelectionChanged += (_, _) =>
            ApplyTransition(_session.SelectSlide(_customShowSlideList.SelectedIndex));
        _customShowSlideList.AllowDrop = true;
        _customShowSlideList.PreviewMouseLeftButtonDown += OnCustomShowSlideListMouseLeftButtonDown;
        _customShowSlideList.PreviewMouseMove += OnCustomShowSlideListMouseMove;
        _customShowSlideList.DragOver += OnCustomShowSlideListDragOver;
        _customShowSlideList.Drop += OnCustomShowSlideListDrop;

        _validationText.Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A));
        _validationText.TextWrapping = TextWrapping.Wrap;
        _validationText.Margin = new Thickness(0, 4, 0, 8);
        ApplySemantic(_validationText, Surface.Field(SlideShowCustomShowDialogField.Validation));

        _renameButton = MakeButton(SlideShowCustomShowDialogAction.Rename, OnRename);
        _updateButton = MakeButton(SlideShowCustomShowDialogAction.UpdateSlides, OnUpdateSlides);
        _deleteButton = MakeButton(SlideShowCustomShowDialogAction.Delete, OnDelete);
        _startButton = MakeButton(SlideShowCustomShowDialogAction.StartShow, OnStartShow);
        _moveUpButton = MakeButton(SlideShowCustomShowDialogAction.MoveUp, () => OnMoveSelectedSlide(-1));
        _moveDownButton = MakeButton(SlideShowCustomShowDialogAction.MoveDown, () => OnMoveSelectedSlide(1));
        _removeButton = MakeButton(SlideShowCustomShowDialogAction.Remove, OnRemoveSelectedSlide);

        Content = BuildContent();
        ApplyTransition(_session.InitialTransition);
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

    public void RemoveSelectedCustomShowSlideForTests() => OnRemoveSelectedSlide();

    public void AddCustomShowSlideOccurrenceForTests(string slideId) => AddSlideOccurrence(slideId);

    public SlideShowCustomShowDragReorderPlan DragReorderCustomShowSlideForTests(
        int sourceSlideIndex,
        int targetDropIndex) =>
        ApplyCustomShowSlideDragReorder(sourceSlideIndex, targetDropIndex);

    internal void PrepareValidationForVisualEvidence()
    {
        _nameBox.Text = string.Empty;
        OnCreate();
    }

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
            Text = Surface.Field(SlideShowCustomShowDialogField.Name).Label,
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
        moveButtons.Children.Add(_removeButton);
        DockPanel.SetDock(moveButtons, Dock.Right);
        orderHeader.Children.Add(moveButtons);
        orderHeader.Children.Add(new TextBlock
        {
            Text = Surface.Field(SlideShowCustomShowDialogField.OrderedSlides).Label,
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
            Text = Surface.Field(SlideShowCustomShowDialogField.AvailableSlides).Label,
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
        buttons.Children.Add(MakeButton(SlideShowCustomShowDialogAction.Create, OnCreate));
        buttons.Children.Add(_renameButton);
        buttons.Children.Add(_updateButton);
        buttons.Children.Add(_deleteButton);
        buttons.Children.Add(_startButton);
        buttons.Children.Add(MakeButton(SlideShowCustomShowDialogAction.Close, Close));
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
        _showList.ItemsSource = plan.CustomShows;

        RebuildSlides(plan.AvailableSlides);

        var selected = _showList.Items
            .OfType<SlideShowCustomShowSessionShowItemPlan>()
            .FirstOrDefault(item => item.Index == plan.SelectedShow?.Index);
        _showList.SelectedItem = selected ??
            _showList.Items.OfType<SlideShowCustomShowSessionShowItemPlan>().FirstOrDefault();
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
                Content = slide.DisplayText,
                Tag = slide.SlideId,
                Margin = new Thickness(0, 2, 0, 2),
            };
            ApplySemantic(
                checkBox,
                Surface.Field(SlideShowCustomShowDialogField.AvailableSlides),
                slide.SlideId);
            _slideCheckBoxes.Add(checkBox);
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), LastChildFill = true };
            var addButton = MakeButton(
                SlideShowCustomShowDialogAction.AddSlide,
                () => AddSlideOccurrence(slide.SlideId),
                slide.SlideId);
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

    private void OnCustomShowSlideListMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is SlideShowCustomShowSessionSlideItemPlan slide)
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
        if (item?.DataContext is SlideShowCustomShowSessionSlideItemPlan slide)
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
        _customShowSlideList.ItemsSource = slides;
    }

    private IEnumerable<string?> SelectedSlideIds() =>
        _slideCheckBoxes
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => checkBox.Tag as string);

    private SlideShowCustomShowSessionShowItemPlan? SelectedShow =>
        _showList.SelectedItem as SlideShowCustomShowSessionShowItemPlan;

    private void SetValidation(string? message) =>
        _validationText.Text = message ?? string.Empty;

    private Button MakeButton(
        SlideShowCustomShowDialogAction actionId,
        Action onClick,
        string? automationSuffix = null)
    {
        var action = Surface.Action(actionId);
        var button = new Button
        {
            Content = action.Label,
            MinWidth = 82,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(8, 3, 8, 3),
            IsDefault = action.IsDefault,
            IsCancel = action.IsCancel,
        };
        AutomationProperties.SetName(button, action.AccessibleName);
        AutomationProperties.SetAutomationId(
            button,
            automationSuffix is null ? action.AutomationId : $"{action.AutomationId}.{automationSuffix}");
        button.Click += (_, _) => onClick();
        return button;
    }

    private static void ApplySemantic(
        DependencyObject control,
        PresentationDialogFieldPlan<SlideShowCustomShowDialogField> field,
        string? automationSuffix = null)
    {
        AutomationProperties.SetName(control, field.AccessibleName);
        AutomationProperties.SetAutomationId(
            control,
            automationSuffix is null ? field.AutomationId : $"{field.AutomationId}.{automationSuffix}");
        if (!string.IsNullOrWhiteSpace(field.HelpText))
            AutomationProperties.SetHelpText(control, field.HelpText);
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

}
