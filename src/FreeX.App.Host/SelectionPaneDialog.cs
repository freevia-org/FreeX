using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal sealed class SelectionPaneDialogItem(SelectionPaneItem item)
{
    public SelectionPaneItem Source { get; } = item;
    public string Name { get; set; } = item.Name;
    public string AutomationId { get; } = CreateAutomationId(item.Kind, item.Id);
    public string VisibilityAutomationId => AutomationId + "VisibilityBox";
    public string NameAutomationId => AutomationId + "NameBox";
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

    private static string CreateAutomationId(SelectionPaneObjectKind kind, Guid id) =>
        $"SelectionPaneItem{kind}{id:N}";
}

internal sealed record SelectionPaneFilterChoice(string Value, string Label);

public sealed partial class SelectionPaneDialog : Window
{
    private const double DialogDefaultWidth = 520d;
    private const double DialogDefaultHeight = 440d;
    private const double DialogMinimumWidth = 460d;
    private const double DialogMinimumHeight = 360d;

    private readonly IReadOnlyList<SelectionPaneItem> _sourceItems;
    private readonly List<SelectionPaneDialogItem> _items;
    private readonly List<SelectionPaneMoveChange> _moveChanges = [];
    private readonly List<SelectionPaneDeleteChange> _deleteChanges = [];
    private readonly ListBox _list = new() { MinHeight = 140 };
    private readonly TextBox _searchBox = new() { MinWidth = 160, Margin = new Thickness(0, 0, 10, 0) };
    private readonly ComboBox _filterBox = new() { MinWidth = 130, Margin = new Thickness(0, 0, 0, 0) };
    private readonly TextBox _renameBox = new() { MinWidth = 160, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _renameButton = new() { Content = UiText.Get("SelectionPane_RenameButton"), MinWidth = 78, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _toggleVisibilityButton = new() { Content = CreateEyeIcon(), Width = 32, Margin = new Thickness(0, 0, 6, 0), ToolTip = UiText.Get("SelectionPane_ToggleVisibilityToolTip") };
    private readonly Button _moveUpButton = new() { Content = UiText.Get("SelectionPane_BringForwardButton"), MinWidth = 104, Margin = new Thickness(0, 0, 6, 6) };
    private readonly Button _moveDownButton = new() { Content = UiText.Get("SelectionPane_SendBackwardButton"), MinWidth = 104, Margin = new Thickness(0, 0, 6, 6) };
    private readonly Button _showAllButton = new() { Content = UiText.Get("SelectionPane_ShowAllButton"), MinWidth = 82, Margin = new Thickness(0, 0, 6, 6) };
    private readonly Button _hideAllButton = new() { Content = UiText.Get("SelectionPane_HideAllButton"), MinWidth = 82, Margin = new Thickness(0, 0, 6, 6) };
    private readonly Button _deleteButton = new() { Content = UiText.Get("SelectionPane_DeleteButton"), MinWidth = 82, Margin = new Thickness(0, 0, 6, 6) };
    private Point? _dragStartPoint;
    private SelectionPaneDialogItem? _dragItem;

    public SelectionPaneDialogResult Result { get; private set; }

    public SelectionPaneDialog(IReadOnlyList<SelectionPaneItem> items)
    {
        _sourceItems = items;
        Result = new SelectionPaneDialogResult(SelectionPaneDialogAction.ApplyVisibility, null, [], [], [], []);
        Title = UiText.Get("SelectionPane_Title");
        Width = DialogDefaultWidth;
        Height = DialogDefaultHeight;
        MinWidth = DialogMinimumWidth;
        MinHeight = DialogMinimumHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, "SelectionPaneDialog");

        _list.Margin = new Thickness(0, 0, 0, 10);
        AutomationProperties.SetName(_list, UiText.Get("SelectionPane_ObjectListAutomationName"));
        AutomationProperties.SetAutomationId(_list, "SelectionPaneObjectList");
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
        AutomationProperties.SetAutomationId(_searchBox, "SelectionPaneSearchBox");
        AutomationProperties.SetHelpText(_searchBox, UiText.Get("SelectionPane_SearchHelpText"));
        _searchBox.TextChanged += (_, _) => ApplySearchAndFilter();
        _filterBox.ItemsSource = CreateFilterChoices();
        _filterBox.DisplayMemberPath = nameof(SelectionPaneFilterChoice.Label);
        _filterBox.SelectedIndex = 0;
        AutomationProperties.SetName(_filterBox, UiText.Get("SelectionPane_FilterAutomationName"));
        AutomationProperties.SetAutomationId(_filterBox, "SelectionPaneFilterBox");
        AutomationProperties.SetHelpText(_filterBox, UiText.Get("SelectionPane_FilterHelpText"));
        _filterBox.SelectionChanged += (_, _) => ApplySearchAndFilter();
        AutomationProperties.SetName(_renameBox, UiText.Get("SelectionPane_ObjectNameAutomationName"));
        AutomationProperties.SetAutomationId(_renameBox, "SelectionPaneRenameBox");
        AutomationProperties.SetHelpText(_renameBox, UiText.Get("SelectionPane_ObjectNameHelpText"));
        AutomationProperties.SetName(_renameButton, UiText.Get("SelectionPane_RenameButtonAutomationName"));
        AutomationProperties.SetAutomationId(_renameButton, "SelectionPaneRenameButton");
        AutomationProperties.SetHelpText(_renameButton, UiText.Get("SelectionPane_RenameButtonHelpText"));
        _renameButton.Click += (_, _) => RenameSelectedItem();
        AutomationProperties.SetName(_toggleVisibilityButton, UiText.Get("SelectionPane_ToggleVisibilityAutomationName"));
        AutomationProperties.SetAutomationId(_toggleVisibilityButton, "SelectionPaneToggleVisibilityButton");
        AutomationProperties.SetHelpText(_toggleVisibilityButton, UiText.Get("SelectionPane_ToggleVisibilityHelpText"));
        _toggleVisibilityButton.Click += (_, _) => ToggleSelectedVisibility();

        AutomationProperties.SetName(_moveUpButton, UiText.Get("SelectionPane_BringForwardAutomationName"));
        AutomationProperties.SetAutomationId(_moveUpButton, "SelectionPaneBringForwardButton");
        AutomationProperties.SetHelpText(_moveUpButton, UiText.Get("SelectionPane_BringForwardHelpText"));
        AutomationProperties.SetName(_moveDownButton, UiText.Get("SelectionPane_SendBackwardAutomationName"));
        AutomationProperties.SetAutomationId(_moveDownButton, "SelectionPaneSendBackwardButton");
        AutomationProperties.SetHelpText(_moveDownButton, UiText.Get("SelectionPane_SendBackwardHelpText"));
        _moveUpButton.Click += (_, _) => AcceptMove(SelectionPaneDialogAction.MoveUp);
        _moveDownButton.Click += (_, _) => AcceptMove(SelectionPaneDialogAction.MoveDown);
        AutomationProperties.SetName(_showAllButton, UiText.Get("SelectionPane_ShowAllAutomationName"));
        AutomationProperties.SetAutomationId(_showAllButton, "SelectionPaneShowAllButton");
        AutomationProperties.SetHelpText(_showAllButton, UiText.Get("SelectionPane_ShowAllHelpText"));
        AutomationProperties.SetName(_hideAllButton, UiText.Get("SelectionPane_HideAllAutomationName"));
        AutomationProperties.SetAutomationId(_hideAllButton, "SelectionPaneHideAllButton");
        AutomationProperties.SetHelpText(_hideAllButton, UiText.Get("SelectionPane_HideAllHelpText"));
        _showAllButton.Click += (_, _) => SetAllVisibility(true);
        _hideAllButton.Click += (_, _) => SetAllVisibility(false);

        AutomationProperties.SetName(_deleteButton, UiText.Get("SelectionPane_DeleteAutomationName"));
        AutomationProperties.SetAutomationId(_deleteButton, "SelectionPaneDeleteButton");
        AutomationProperties.SetHelpText(_deleteButton, UiText.Get("SelectionPane_DeleteHelpText"));
        _deleteButton.Click += (_, _) => DeleteSelectedItem();

        var okButton = new Button { Content = UiText.Ok, Width = 78, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        AutomationProperties.SetName(okButton, UiText.Get("SelectionPane_OkAutomationName"));
        AutomationProperties.SetAutomationId(okButton, "SelectionPaneOkButton");
        AutomationProperties.SetHelpText(okButton, UiText.Get("SelectionPane_OkHelpText"));
        okButton.Click += (_, _) => AcceptVisibility();
        var cancelButton = new Button { Content = UiText.Cancel, Width = 78, IsCancel = true };
        AutomationProperties.SetName(cancelButton, UiText.Get("SelectionPane_CancelAutomationName"));
        AutomationProperties.SetAutomationId(cancelButton, "SelectionPaneCancelButton");
        AutomationProperties.SetHelpText(cancelButton, UiText.Get("SelectionPane_CancelHelpText"));

        var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddGridChild(searchRow, new Label { Content = UiText.Get("SelectionPane_SearchLabel"), Target = _searchBox, Padding = new Thickness(0, 4, 6, 0) }, 0);
        AddGridChild(searchRow, _searchBox, 1);
        AddGridChild(searchRow, new Label { Content = UiText.Get("SelectionPane_FilterLabel"), Target = _filterBox, Padding = new Thickness(0, 4, 6, 0) }, 2);
        AddGridChild(searchRow, _filterBox, 3);

        var renameRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        renameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        renameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        renameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        renameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddGridChild(renameRow, new Label { Content = UiText.Get("SelectionPane_NameLabel"), Target = _renameBox, Padding = new Thickness(0, 4, 6, 0) }, 0);
        AddGridChild(renameRow, _renameBox, 1);
        AddGridChild(renameRow, _renameButton, 2);
        AddGridChild(renameRow, _toggleVisibilityButton, 3);

        var commandRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        commandRow.Children.Add(_showAllButton);
        commandRow.Children.Add(_hideAllButton);
        commandRow.Children.Add(_moveUpButton);
        commandRow.Children.Add(_moveDownButton);
        commandRow.Children.Add(_deleteButton);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(cancelButton);

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 140 });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddGridChild(root, searchRow, 0, isRow: true);
        AddGridChild(root, _list, 1, isRow: true);
        AddGridChild(root, renameRow, 2, isRow: true);
        AddGridChild(root, commandRow, 3, isRow: true);
        AddGridChild(root, buttonRow, 4, isRow: true);
        Content = root;
        UpdateMoveButtons();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private static void AddGridChild(Grid grid, UIElement child, int index, bool isRow = false)
    {
        if (isRow)
            Grid.SetRow(child, index);
        else
            Grid.SetColumn(child, index);

        grid.Children.Add(child);
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
        border.SetBinding(AutomationProperties.AutomationIdProperty, new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.AutomationId)));

        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(FrameworkElement.MinHeightProperty, 24.0);

        var checkBox = new FrameworkElementFactory(typeof(CheckBox));
        checkBox.SetValue(CheckBox.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        checkBox.SetValue(FrameworkElement.WidthProperty, 24.0);
        checkBox.SetValue(CheckBox.ToolTipProperty, UiText.Get("SelectionPane_ItemVisibilityToolTip"));
        checkBox.SetValue(AutomationProperties.NameProperty, UiText.Get("SelectionPane_ItemVisibilityAutomationName"));
        checkBox.SetBinding(AutomationProperties.AutomationIdProperty, new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.VisibilityAutomationId)));
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
        name.SetBinding(AutomationProperties.AutomationIdProperty, new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.NameAutomationId)));
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
