using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed partial class CustomShowDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly SlideShowCustomShowDialogNativeComposition<FrameworkElement, DockPanel> _renderer;
    private readonly ListBox _showList = new();
    private readonly TextBox _nameBox = new();
    private readonly ListBox _customShowSlideList = new();
    private readonly StackPanel _slidePanel = new();
    private readonly TextBlock _validationText = new();
    private Point? _customShowSlideDragStartPoint;
    private int _customShowSlideDragSourceIndex = -1;

    private PresentationDialogSurfacePlan<
        SlideShowCustomShowDialogField,
        SlideShowCustomShowDialogAction> Surface => _renderer.Surface;

    public CustomShowDialog(
        SlideShowCustomShowSession customShowSession,
        Func<string?, bool>? tryStartShow = null)
    {
        ArgumentNullException.ThrowIfNull(customShowSession);
        SlideShowCustomShowDialogSession session =
            customShowSession.CreateDialogSession(tryStartShow ?? (_ => false));
        _renderer = new(
            session,
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
            static (control, isEnabled) => control.IsEnabled = isEnabled,
            () => _nameBox.Text,
            Close,
            _slidePanel.Children.Clear,
            CreateAvailableSlideRow,
            row => _slidePanel.Children.Add(row),
            CreateNativeButton);

        Title = Surface.Title;
        AutomationProperties.SetName(this, Surface.AccessibleName);
        AutomationProperties.SetAutomationId(this, Surface.AutomationId);
        Width = SlideShowCustomShowDialogVisualMetrics.WpfWindowWidth;
        Height = SlideShowCustomShowDialogVisualMetrics.WpfWindowHeight;
        MinWidth = SlideShowCustomShowDialogVisualMetrics.MinimumWindowWidth;
        MinHeight = SlideShowCustomShowDialogVisualMetrics.MinimumWindowHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = FreePBrushes.SheetSurface;

        _showList.Margin = new Thickness(
            0,
            0,
            SlideShowCustomShowDialogVisualMetrics.ShowListRightGap,
            0);
        PresentationDialogControlAdapter.ApplySemantic(_showList, Surface.Field(SlideShowCustomShowDialogField.CustomShows));
        _showList.SelectionChanged += (_, _) => _renderer.Controller.SelectShow();

        _nameBox.MinWidth = SlideShowCustomShowDialogVisualMetrics.NameMinimumWidth;
        _nameBox.Margin = new Thickness(
            0,
            0,
            0,
            SlideShowCustomShowDialogVisualMetrics.NameBottomMargin);
        PresentationDialogControlAdapter.ApplySemantic(_nameBox, Surface.Field(SlideShowCustomShowDialogField.Name));

        _customShowSlideList.MinHeight = SlideShowCustomShowDialogVisualMetrics.OrderedSlidesMinimumHeight;
        PresentationDialogControlAdapter.ApplySemantic(_customShowSlideList, Surface.Field(SlideShowCustomShowDialogField.OrderedSlides));
        _customShowSlideList.SelectionChanged += (_, _) => _renderer.Controller.SelectSlide();
        _customShowSlideList.AllowDrop = true;
        _customShowSlideList.PreviewMouseLeftButtonDown += OnCustomShowSlideListMouseLeftButtonDown;
        _customShowSlideList.PreviewMouseMove += OnCustomShowSlideListMouseMove;
        _customShowSlideList.DragOver += OnCustomShowSlideListDragOver;
        _customShowSlideList.Drop += OnCustomShowSlideListDrop;

        _validationText.Foreground = FreePBrushes.Accent;
        _validationText.TextWrapping = TextWrapping.Wrap;
        _validationText.Margin = new Thickness(
            0,
            SlideShowCustomShowDialogVisualMetrics.ValidationTopMargin,
            0,
            SlideShowCustomShowDialogVisualMetrics.ValidationBottomMargin);
        PresentationDialogControlAdapter.ApplySemantic(_validationText, Surface.Field(SlideShowCustomShowDialogField.Validation));

        Content = BuildContent();
        _renderer.Controller.Initialize();
    }

    public int RenderedCustomShowCount => _showList.Items.Count;

    public int RenderedSlideOptionCount => _renderer.AvailableSlides.Controls.Count;

    public int RenderedCustomShowSlideCount => _customShowSlideList.Items.Count;

    public int SelectedCustomShowSlideIndex => _renderer.Form.SelectedSlideIndex;

    public string ValidationMessage => _validationText.Text;

    private UIElement BuildContent()
    {
        var root = new Grid
        {
            Margin = new Thickness(SlideShowCustomShowDialogVisualMetrics.RootInset),
        };
        root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(SlideShowCustomShowDialogVisualMetrics.ShowListColumnWidth),
        });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(_showList, 0);
        Grid.SetColumn(_showList, 0);
        root.Children.Add(_showList);

        var editor = new Grid();
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(SlideShowCustomShowDialogVisualMetrics.OrderedSlidesRowHeight),
        });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var namePanel = new StackPanel();
        namePanel.Children.Add(new TextBlock
        {
            Text = Surface.Field(SlideShowCustomShowDialogField.Name).Label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(
                0,
                0,
                0,
                SlideShowCustomShowDialogVisualMetrics.LabelBottomMargin),
        });
        namePanel.Children.Add(_nameBox);
        Grid.SetRow(namePanel, 0);
        editor.Children.Add(namePanel);

        var orderHeader = new DockPanel
        {
            Margin = new Thickness(
                0,
                SlideShowCustomShowDialogVisualMetrics.OrderHeaderTopMargin,
                0,
                SlideShowCustomShowDialogVisualMetrics.LabelBottomMargin),
            LastChildFill = true,
        };
        var moveButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        moveButtons.Children.Add(_renderer.Buttons.MoveUp);
        moveButtons.Children.Add(_renderer.Buttons.MoveDown);
        moveButtons.Children.Add(_renderer.Buttons.Remove);
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
            Margin = new Thickness(
                0,
                SlideShowCustomShowDialogVisualMetrics.AvailableSlidesTopMargin,
                0,
                SlideShowCustomShowDialogVisualMetrics.LabelBottomMargin),
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
            Margin = new Thickness(
                0,
                SlideShowCustomShowDialogVisualMetrics.ActionRowTopMargin,
                0,
                0),
        };
        buttons.Children.Add(_renderer.CreateButton(SlideShowCustomShowDialogAction.Create, _renderer.Controller.Create));
        buttons.Children.Add(_renderer.Buttons.Rename);
        buttons.Children.Add(_renderer.Buttons.Update);
        buttons.Children.Add(_renderer.Buttons.Delete);
        buttons.Children.Add(_renderer.Buttons.Start);
        buttons.Children.Add(_renderer.CreateButton(SlideShowCustomShowDialogAction.Close, Close));
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);

        return root;
    }

    private SlideShowCustomShowAvailableSlideNativeRow<FrameworkElement, DockPanel>
        CreateAvailableSlideRow(SlideShowCustomShowSlideOption slide)
    {
        var checkBox = new CheckBox
        {
            Content = slide.DisplayText,
            Tag = slide.SlideId,
            Margin = new Thickness(
                0,
                SlideShowCustomShowDialogVisualMetrics.AvailableSlideVerticalMargin,
                0,
                SlideShowCustomShowDialogVisualMetrics.AvailableSlideVerticalMargin),
            Height = SlideShowCustomShowDialogVisualMetrics.AvailableSlideControlHeight,
            MinHeight = SlideShowCustomShowDialogVisualMetrics.AvailableSlideControlHeight,
            MaxHeight = SlideShowCustomShowDialogVisualMetrics.AvailableSlideControlHeight,
            Padding = new Thickness(0),
        };
        PresentationDialogControlAdapter.ApplySemantic(
            checkBox,
            Surface.Field(SlideShowCustomShowDialogField.AvailableSlides, slide.SlideId));
        var row = new DockPanel
        {
            Margin = checkBox.Margin,
            LastChildFill = true,
        };
        var addButton = (Button)_renderer.CreateButton(
            SlideShowCustomShowDialogAction.AddSlide,
            () => _renderer.Actions.Execute(SlideShowCustomShowDialogAction.AddSlide, slide.SlideId),
            slide.SlideId);
        addButton.MinWidth = SlideShowCustomShowDialogVisualMetrics.AddSlideButtonMinimumWidth;
        DockPanel.SetDock(addButton, Dock.Right);
        row.Children.Add(addButton);
        row.Children.Add(checkBox);
        return new(slide.SlideId, checkBox, row);
    }

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
        int targetDropIndex) =>
        _renderer.Controller.Reorder(sourceSlideIndex, targetDropIndex);

    private static Button CreateNativeButton(
        PresentationDialogActionPlan<SlideShowCustomShowDialogAction> action,
        Action onClick)
    {
        var button = new Button
        {
            Content = action.Label,
            MinWidth = SlideShowCustomShowDialogVisualMetrics.ActionButtonMinimumWidth,
            Margin = new Thickness(
                SlideShowCustomShowDialogVisualMetrics.ActionSpacing,
                0,
                0,
                0),
            Padding = new Thickness(
                SlideShowCustomShowDialogVisualMetrics.ActionButtonHorizontalPadding,
                SlideShowCustomShowDialogVisualMetrics.ActionButtonVerticalPadding,
                SlideShowCustomShowDialogVisualMetrics.ActionButtonHorizontalPadding,
                SlideShowCustomShowDialogVisualMetrics.ActionButtonVerticalPadding),
            IsDefault = action.IsDefault,
            IsCancel = action.IsCancel,
        };
        AutomationProperties.SetName(button, action.AccessibleName);
        AutomationProperties.SetAutomationId(button, action.AutomationId);
        button.Click += (_, _) => onClick();
        return button;
    }

    private static void SetItemsSource(FrameworkElement control, object? items) =>
        ((ItemsControl)control).ItemsSource = items as System.Collections.IEnumerable;

    private static void SetText(FrameworkElement control, string text)
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
