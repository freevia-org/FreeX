using Avalonia;
using Avalonia.Automation;
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

    private readonly SlideShowCustomShowDialogSession _session;
    private readonly SlideShowCustomShowDialogFormSession<Control> _formSession;
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

    private PresentationDialogSurfacePlan<
        SlideShowCustomShowDialogField,
        SlideShowCustomShowDialogAction> Surface => _session.Surface;

    public CustomShowDialog(
        SlideShowCustomShowSession customShowSession,
        Func<string?, bool>? tryStartShow = null)
    {
        ArgumentNullException.ThrowIfNull(customShowSession);
        _session = customShowSession.CreateDialogSession(tryStartShow ?? (_ => false));
        _formSession = new(
            _showList,
            _customShowSlideList,
            _nameBox,
            _validationText,
            SetItemsSource,
            static (control, index) => ((ListBox)control).SelectedIndex = index,
            static control => ((ListBox)control).SelectedIndex,
            static control => ((ListBox)control).SelectedItem,
            SetText,
            static (control, isChecked) => ((CheckBox)control).IsChecked = isChecked,
            static control => ((CheckBox)control).IsChecked == true,
            static (control, isEnabled) => control.IsEnabled = isEnabled);

        Title = Surface.Title;
        AutomationProperties.SetName(this, Surface.AccessibleName);
        AutomationProperties.SetAutomationId(this, Surface.AutomationId);
        Width = 625.3333333333334;
        Height = 402.6666666666667;
        MinWidth = 560;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _showList.Margin = new Thickness(0, 0, 10, 0);
        ApplyListChrome(_showList);
        ApplySemantic(_showList, Surface.Field(SlideShowCustomShowDialogField.CustomShows));
        _showList.SelectionChanged += (_, _) => OnSelectedShowChanged();

        _nameBox.MinWidth = 260;
        _nameBox.Margin = new Thickness(0, 0, 0, 8);
        AvaloniaCompactDialogChrome.ApplyTextBox(_nameBox, DialogChromeStyle);
        ApplySemantic(_nameBox, Surface.Field(SlideShowCustomShowDialogField.Name));

        _customShowSlideList.MinHeight = 92;
        ApplyListChrome(_customShowSlideList);
        ApplySemantic(_customShowSlideList, Surface.Field(SlideShowCustomShowDialogField.OrderedSlides));
        _customShowSlideList.SelectionChanged += (_, _) =>
            ApplyTransition(_session.SelectSlide(_formSession.SelectedSlideIndex));
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

    internal int RenderedCustomShowCount => _showList.Items.Count;

    internal int RenderedSlideOptionCount => _slideCheckBoxes.Count;

    internal int RenderedCustomShowSlideCount => _customShowSlideList.Items.Count;

    internal int SelectedCustomShowSlideIndex => _formSession.SelectedSlideIndex;

    internal string ValidationMessage => _validationText.Text ?? string.Empty;

    internal void SelectCustomShowSlideForTests(int index) =>
        _formSession.SelectSlide(index);

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
            Text = Surface.Field(SlideShowCustomShowDialogField.Name).Label,
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
            Text = Surface.Field(SlideShowCustomShowDialogField.OrderedSlides).Label,
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
            Text = Surface.Field(SlideShowCustomShowDialogField.AvailableSlides).Label,
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
                MakeButton(SlideShowCustomShowDialogAction.Create, OnCreate),
                _renameButton,
                _updateButton,
                _deleteButton,
                _startButton,
                MakeButton(SlideShowCustomShowDialogAction.Close, Close),
            },
        };
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);

        return root;
    }

    private void ApplyTransition(SlideShowCustomShowDialogSessionTransition transition)
        => SlideShowCustomShowDialogTransitionDispatcher.Dispatch(
            transition,
            RenderFullPlan,
            RenderSelectedShowPlan,
            ApplySlideSelection,
            SetValidation,
            () => Close());

    private void RenderFullPlan(SlideShowCustomShowSessionPlan plan)
    {
        RebuildSlides(plan.AvailableSlides);
        _formSession.ApplyFullPlan(plan);
    }

    private void RebuildSlides(IReadOnlyList<SlideShowCustomShowSlideOption> slides)
    {
        _slidePanel.Children.Clear();
        _slideCheckBoxes.Clear();
        _formSession.ClearAvailableSlides();

        foreach (var slide in slides)
        {
            var checkBox = new CheckBox
            {
                Content = slide.DisplayText,
                Tag = slide.SlideId,
                Margin = new Thickness(0, 2, 0, 2),
            };
            AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, DialogChromeStyle);
            checkBox.Height = 20;
            checkBox.MinHeight = 20;
            checkBox.MaxHeight = 20;
            checkBox.Padding = new Thickness(0);
            ApplySemantic(
                checkBox,
                Surface.Field(SlideShowCustomShowDialogField.AvailableSlides, slide.SlideId));
            _slideCheckBoxes.Add(checkBox);
            _formSession.RegisterAvailableSlide(slide.SlideId, checkBox);
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
        ApplyTransition(_session.SelectShow(_formSession.SelectedShowIndex));
    }

    private void RenderSelectedShowPlan(SlideShowCustomShowSessionPlan plan) =>
        _formSession.ApplySelectedShowPlan(plan);

    private void ApplySlideSelection(SlideShowCustomShowSessionPlan plan) =>
        _formSession.ApplySlideSelection(plan);

    private void OnCreate() =>
        ApplyTransition(_session.Create(_nameBox.Text, _formSession.SelectedSlideIds()));

    private void OnRename() =>
        ApplyTransition(_session.Rename(_nameBox.Text));

    private void OnUpdateSlides() =>
        ApplyTransition(_session.UpdateSlides(_formSession.SelectedSlideIds()));

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
            item?.DataContext is SlideShowCustomShowSessionSlideItemPlan slide)
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
        if (item?.DataContext is SlideShowCustomShowSessionSlideItemPlan slide)
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
        if (item?.DataContext is SlideShowCustomShowSessionSlideItemPlan slide)
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

    private void SetValidation(string? message) =>
        _formSession.SetValidation(message);

    private static void ApplyListChrome(ListBox listBox)
    {
        AvaloniaCompactDialogChrome.ApplyListBox(listBox, DialogChromeStyle);
        listBox.Background = Brushes.White;
        listBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
        listBox.BorderThickness = new Thickness(1);
    }

    private Button MakeButton(
        SlideShowCustomShowDialogAction actionId,
        Action onClick,
        string? automationSuffix = null)
    {
        var action = Surface.Action(actionId, automationSuffix);
        var button = new Button
        {
            Content = action.Label,
            IsDefault = action.IsDefault,
            IsCancel = action.IsCancel,
        };
        AutomationProperties.SetName(button, action.AccessibleName);
        AutomationProperties.SetAutomationId(button, action.AutomationId);
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            DialogChromeStyle,
            minWidth: 82,
            isDefault: action.IsDefault);
        button.Click += (_, _) => onClick();
        if (automationSuffix is null)
            _formSession.RegisterAction(actionId, button);
        return button;
    }

    private static void ApplySemantic(
        Control control,
        PresentationDialogFieldPlan<SlideShowCustomShowDialogField> field)
    {
        AutomationProperties.SetName(control, field.AccessibleName);
        AutomationProperties.SetAutomationId(control, field.AutomationId);
        if (!string.IsNullOrWhiteSpace(field.HelpText))
            AutomationProperties.SetHelpText(control, field.HelpText);
    }

    private static void SetItemsSource(Control control, object? items) =>
        ((ItemsControl)control).ItemsSource = items as System.Collections.IEnumerable;

    private static void SetText(Control control, string text)
    {
        switch (control)
        {
            case TextBox textBox:
                textBox.Text = text;
                break;
            case TextBlock textBlock:
                textBlock.Text = text;
                break;
            default:
                throw new InvalidOperationException($"Unsupported custom show text control: {control.GetType().Name}.");
        }
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

}
