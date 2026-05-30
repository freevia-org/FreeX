using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public enum SelectionPaneDialogAction
{
    ApplyVisibility,
    MoveUp,
    MoveDown
}

public sealed record SelectionPaneVisibilityChange(SelectionPaneObjectKind Kind, Guid Id, bool IsVisible);

public sealed record SelectionPaneRenameChange(SelectionPaneObjectKind Kind, Guid Id, string Name);

public sealed record SelectionPaneMoveChange(SelectionPaneObjectKind Kind, Guid Id, bool Forward);

public sealed record SelectionPaneDialogResult(
    SelectionPaneDialogAction Action,
    SelectionPaneItem? Target,
    IReadOnlyList<SelectionPaneVisibilityChange> VisibilityChanges,
    IReadOnlyList<SelectionPaneRenameChange> RenameChanges,
    IReadOnlyList<SelectionPaneMoveChange> MoveChanges);

internal sealed class SelectionPaneDialogItem(SelectionPaneItem item)
{
    public SelectionPaneItem Source { get; } = item;
    public string Name { get; set; } = item.Name;
    public string Kind => Source.Kind switch
    {
        SelectionPaneObjectKind.Chart => UiText.Get("SelectionPane_ObjectKindChart"),
        SelectionPaneObjectKind.Picture => UiText.Get("SelectionPane_ObjectKindPicture"),
        SelectionPaneObjectKind.Shape => UiText.Get("SelectionPane_ObjectKindShape"),
        SelectionPaneObjectKind.TextBox => UiText.Get("SelectionPane_ObjectKindTextBox"),
        _ => Source.Kind.ToString()
    };
    public bool IsVisible { get; set; } = item.IsVisible;
    public bool IsDropBefore { get; set; }
    public bool IsDropAfter { get; set; }
}

internal sealed record SelectionPaneFilterChoice(string Value, string Label);

public sealed partial class SelectionPaneDialog : Window
{
    private readonly IReadOnlyList<SelectionPaneItem> _sourceItems;
    private readonly List<SelectionPaneDialogItem> _items;
    private readonly List<SelectionPaneMoveChange> _moveChanges = [];
    private readonly ListBox _list = new();
    private readonly TextBox _searchBox = new() { Width = 180, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _filterBox = new() { Width = 110, Margin = new Thickness(0, 0, 0, 0) };
    private readonly TextBox _renameBox = new() { Width = 180, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _renameButton = new() { Content = UiText.Get("SelectionPane_RenameButton"), Width = 78, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _toggleVisibilityButton = new() { Content = CreateEyeIcon(), Width = 32, Margin = new Thickness(0, 0, 6, 0), ToolTip = UiText.Get("SelectionPane_ToggleVisibilityToolTip") };
    private readonly Button _moveUpButton = new() { Content = UiText.Get("SelectionPane_BringForwardButton"), Width = 104, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _moveDownButton = new() { Content = UiText.Get("SelectionPane_SendBackwardButton"), Width = 104, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _showAllButton = new() { Content = UiText.Get("SelectionPane_ShowAllButton"), Width = 82, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _hideAllButton = new() { Content = UiText.Get("SelectionPane_HideAllButton"), Width = 82, Margin = new Thickness(0, 0, 6, 0) };
    private Point? _dragStartPoint;
    private SelectionPaneDialogItem? _dragItem;

    public SelectionPaneDialogResult Result { get; private set; }

    public SelectionPaneDialog(IReadOnlyList<SelectionPaneItem> items)
    {
        _sourceItems = items;
        Result = new SelectionPaneDialogResult(SelectionPaneDialogAction.ApplyVisibility, null, [], [], []);
        Title = UiText.Get("SelectionPane_Title");
        Width = 380;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _list.Margin = new Thickness(0, 0, 0, 10);
        AutomationProperties.SetName(_list, UiText.Get("SelectionPane_ObjectListAutomationName"));
        AutomationProperties.SetHelpText(_list, UiText.Get("SelectionPane_ObjectListHelpText"));
        _list.AllowDrop = true;
        _list.PreviewMouseLeftButtonDown += List_PreviewMouseLeftButtonDown;
        _list.MouseMove += List_MouseMove;
        _list.DragOver += List_DragOver;
        _list.DragLeave += List_DragLeave;
        _list.Drop += List_Drop;
        _list.KeyDown += List_KeyDown;
        _items = items.Select(item => new SelectionPaneDialogItem(item)).ToList();
        _list.ItemsSource = _items;
        _list.SelectionChanged += (_, _) =>
        {
            UpdateMoveButtons();
            UpdateRenameBox();
        };
        if (_list.Items.Count > 0)
            _list.SelectedIndex = 0;
        _list.ItemTemplate = CreateItemTemplate();
        AutomationProperties.SetName(_searchBox, UiText.Get("SelectionPane_SearchAutomationName"));
        AutomationProperties.SetHelpText(_searchBox, UiText.Get("SelectionPane_SearchHelpText"));
        _searchBox.TextChanged += (_, _) => ApplySearchAndFilter();
        _filterBox.ItemsSource = CreateFilterChoices();
        _filterBox.DisplayMemberPath = nameof(SelectionPaneFilterChoice.Label);
        _filterBox.SelectedIndex = 0;
        AutomationProperties.SetName(_filterBox, UiText.Get("SelectionPane_FilterAutomationName"));
        AutomationProperties.SetHelpText(_filterBox, UiText.Get("SelectionPane_FilterHelpText"));
        _filterBox.SelectionChanged += (_, _) => ApplySearchAndFilter();
        AutomationProperties.SetName(_renameBox, UiText.Get("SelectionPane_ObjectNameAutomationName"));
        AutomationProperties.SetHelpText(_renameBox, UiText.Get("SelectionPane_ObjectNameHelpText"));
        AutomationProperties.SetName(_renameButton, UiText.Get("SelectionPane_RenameButtonAutomationName"));
        AutomationProperties.SetHelpText(_renameButton, UiText.Get("SelectionPane_RenameButtonHelpText"));
        _renameButton.Click += (_, _) => RenameSelectedItem();
        AutomationProperties.SetName(_toggleVisibilityButton, UiText.Get("SelectionPane_ToggleVisibilityAutomationName"));
        AutomationProperties.SetHelpText(_toggleVisibilityButton, UiText.Get("SelectionPane_ToggleVisibilityHelpText"));
        _toggleVisibilityButton.Click += (_, _) => ToggleSelectedVisibility();

        AutomationProperties.SetName(_moveUpButton, UiText.Get("SelectionPane_BringForwardAutomationName"));
        AutomationProperties.SetHelpText(_moveUpButton, UiText.Get("SelectionPane_BringForwardHelpText"));
        AutomationProperties.SetName(_moveDownButton, UiText.Get("SelectionPane_SendBackwardAutomationName"));
        AutomationProperties.SetHelpText(_moveDownButton, UiText.Get("SelectionPane_SendBackwardHelpText"));
        _moveUpButton.Click += (_, _) => AcceptMove(SelectionPaneDialogAction.MoveUp);
        _moveDownButton.Click += (_, _) => AcceptMove(SelectionPaneDialogAction.MoveDown);
        AutomationProperties.SetName(_showAllButton, UiText.Get("SelectionPane_ShowAllAutomationName"));
        AutomationProperties.SetHelpText(_showAllButton, UiText.Get("SelectionPane_ShowAllHelpText"));
        AutomationProperties.SetName(_hideAllButton, UiText.Get("SelectionPane_HideAllAutomationName"));
        AutomationProperties.SetHelpText(_hideAllButton, UiText.Get("SelectionPane_HideAllHelpText"));
        _showAllButton.Click += (_, _) => SetAllVisibility(true);
        _hideAllButton.Click += (_, _) => SetAllVisibility(false);

        var okButton = new Button { Content = UiText.Ok, Width = 78, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        AutomationProperties.SetName(okButton, UiText.Get("SelectionPane_OkAutomationName"));
        AutomationProperties.SetHelpText(okButton, UiText.Get("SelectionPane_OkHelpText"));
        okButton.Click += (_, _) => AcceptVisibility();
        var cancelButton = new Button { Content = UiText.Cancel, Width = 78, IsCancel = true };
        AutomationProperties.SetName(cancelButton, UiText.Get("SelectionPane_CancelAutomationName"));
        AutomationProperties.SetHelpText(cancelButton, UiText.Get("SelectionPane_CancelHelpText"));

        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        searchRow.Children.Add(new Label { Content = UiText.Get("SelectionPane_SearchLabel"), Target = _searchBox, Padding = new Thickness(0, 4, 6, 0) });
        searchRow.Children.Add(_searchBox);
        searchRow.Children.Add(new Label { Content = UiText.Get("SelectionPane_FilterLabel"), Target = _filterBox, Padding = new Thickness(0, 4, 6, 0) });
        searchRow.Children.Add(_filterBox);

        var renameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        renameRow.Children.Add(new Label { Content = UiText.Get("SelectionPane_NameLabel"), Target = _renameBox, Padding = new Thickness(0, 4, 6, 0) });
        renameRow.Children.Add(_renameBox);
        renameRow.Children.Add(_renameButton);
        renameRow.Children.Add(_toggleVisibilityButton);

        var moveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        moveRow.Children.Add(_moveUpButton);
        moveRow.Children.Add(_moveDownButton);

        var visibilityRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        visibilityRow.Children.Add(_showAllButton);
        visibilityRow.Children.Add(_hideAllButton);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(cancelButton);

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(searchRow);
        stack.Children.Add(_list);
        stack.Children.Add(renameRow);
        stack.Children.Add(visibilityRow);
        stack.Children.Add(moveRow);
        stack.Children.Add(buttonRow);
        Content = stack;
        UpdateMoveButtons();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.FocusAndSelect(_searchBox);
    }

    private static IReadOnlyList<SelectionPaneFilterChoice> CreateFilterChoices() =>
    [
        new(SelectionPaneFilterValues.All, UiText.Get("SelectionPane_FilterAll")),
        new(SelectionPaneFilterValues.Visible, UiText.Get("SelectionPane_FilterVisible")),
        new(SelectionPaneFilterValues.Hidden, UiText.Get("SelectionPane_FilterHidden")),
        new(SelectionPaneFilterValues.Charts, UiText.Get("SelectionPane_FilterCharts")),
        new(SelectionPaneFilterValues.Pictures, UiText.Get("SelectionPane_FilterPictures")),
        new(SelectionPaneFilterValues.Shapes, UiText.Get("SelectionPane_FilterShapes")),
        new(SelectionPaneFilterValues.TextBoxes, UiText.Get("SelectionPane_FilterTextBoxes"))
    ];

    private static DataTemplate CreateItemTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0xC5)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        border.SetValue(Border.PaddingProperty, new Thickness(0, 2, 0, 2));

        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(FrameworkElement.MinHeightProperty, 24.0);

        var checkBox = new FrameworkElementFactory(typeof(CheckBox));
        checkBox.SetValue(CheckBox.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        checkBox.SetValue(FrameworkElement.WidthProperty, 24.0);
        checkBox.SetValue(CheckBox.ToolTipProperty, UiText.Get("SelectionPane_ItemVisibilityToolTip"));
        checkBox.SetValue(AutomationProperties.NameProperty, UiText.Get("SelectionPane_ItemVisibilityAutomationName"));
        checkBox.SetValue(AutomationProperties.HelpTextProperty, UiText.Get("SelectionPane_ItemVisibilityHelpText"));
        checkBox.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.IsVisible)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        panel.AppendChild(checkBox);

        var name = new FrameworkElementFactory(typeof(TextBox));
        name.SetValue(TextBox.MarginProperty, new Thickness(8, 0, 0, 0));
        name.SetValue(TextBox.WidthProperty, 160.0);
        name.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
        name.SetValue(TextBox.BackgroundProperty, Brushes.Transparent);
        name.SetValue(TextBox.ToolTipProperty, UiText.Get("SelectionPane_ItemRenameToolTip"));
        name.SetValue(AutomationProperties.NameProperty, UiText.Get("SelectionPane_ObjectNameAutomationName"));
        name.SetValue(AutomationProperties.HelpTextProperty, UiText.Get("SelectionPane_ObjectNameHelpText"));
        name.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.Name))
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        panel.AppendChild(name);

        var kind = new FrameworkElementFactory(typeof(TextBlock));
        kind.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));
        kind.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Gray);
        kind.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.Kind)));
        panel.AppendChild(kind);

        border.AppendChild(panel);

        var beforeTrigger = new DataTrigger
        {
            Binding = new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.IsDropBefore)),
            Value = true
        };
        beforeTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0, 2, 0, 0)));

        var afterTrigger = new DataTrigger
        {
            Binding = new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.IsDropAfter)),
            Value = true
        };
        afterTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2)));

        return new DataTemplate
        {
            VisualTree = border,
            Triggers = { beforeTrigger, afterTrigger }
        };
    }

    private static Viewbox CreateEyeIcon()
    {
        return new Viewbox
        {
            Width = 14,
            Height = 14,
            Child = new Grid
            {
                Width = 16,
                Height = 16,
                Children =
                {
                    new Path
                    {
                        Data = Geometry.Parse("M1.5,8 C3.7,4.2 5.9,3 8,3 C10.1,3 12.3,4.2 14.5,8 C12.3,11.8 10.1,13 8,13 C5.9,13 3.7,11.8 1.5,8 Z"),
                        Stroke = Brushes.Black,
                        StrokeThickness = 1.1,
                        Fill = Brushes.Transparent
                    },
                    new Ellipse
                    {
                        Width = 4,
                        Height = 4,
                        Fill = Brushes.Black,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    }
                }
            }
        };
    }
}
