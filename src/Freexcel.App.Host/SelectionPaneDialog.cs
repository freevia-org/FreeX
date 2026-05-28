using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
    public string Kind => Source.Kind.ToString();
    public bool IsVisible { get; set; } = item.IsVisible;
}

public sealed partial class SelectionPaneDialog : Window
{
    private readonly IReadOnlyList<SelectionPaneItem> _sourceItems;
    private readonly List<SelectionPaneDialogItem> _items;
    private readonly List<SelectionPaneMoveChange> _moveChanges = [];
    private readonly ListBox _list = new();
    private readonly TextBox _searchBox = new() { Width = 180, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _filterBox = new() { Width = 110, Margin = new Thickness(0, 0, 0, 0) };
    private readonly TextBox _renameBox = new() { Width = 180, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _renameButton = new() { Content = "_Rename", Width = 78, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _toggleVisibilityButton = new() { Content = CreateEyeIcon(), Width = 32, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Toggle visibility" };
    private readonly Button _moveUpButton = new() { Content = "_Bring Forward", Width = 104, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _moveDownButton = new() { Content = "Send _Backward", Width = 104, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _showAllButton = new() { Content = "Show _All", Width = 82, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _hideAllButton = new() { Content = "_Hide All", Width = 82, Margin = new Thickness(0, 0, 6, 0) };
    private Point? _dragStartPoint;
    private SelectionPaneDialogItem? _dragItem;

    public SelectionPaneDialogResult Result { get; private set; }

    public SelectionPaneDialog(IReadOnlyList<SelectionPaneItem> items)
    {
        _sourceItems = items;
        Result = new SelectionPaneDialogResult(SelectionPaneDialogAction.ApplyVisibility, null, [], [], []);
        Title = "Selection Pane";
        Width = 380;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _list.Margin = new Thickness(0, 0, 0, 10);
        AutomationProperties.SetName(_list, "Objects");
        AutomationProperties.SetHelpText(_list, "Workbook objects available for selection, rename, visibility, and stacking order changes.");
        _list.AllowDrop = true;
        _list.PreviewMouseLeftButtonDown += List_PreviewMouseLeftButtonDown;
        _list.MouseMove += List_MouseMove;
        _list.DragOver += List_DragOver;
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
        AutomationProperties.SetName(_searchBox, "Search objects");
        AutomationProperties.SetHelpText(_searchBox, "Filter selection pane objects by name or object type.");
        _searchBox.TextChanged += (_, _) => ApplySearchAndFilter();
        _filterBox.ItemsSource = new[] { "All", "Visible", "Hidden", "Charts", "Pictures", "Shapes", "Text Boxes" };
        _filterBox.SelectedIndex = 0;
        AutomationProperties.SetName(_filterBox, "Object filter");
        AutomationProperties.SetHelpText(_filterBox, "Show all objects or limit the list to visible, hidden, or a specific object type.");
        _filterBox.SelectionChanged += (_, _) => ApplySearchAndFilter();
        AutomationProperties.SetName(_renameBox, "Object name");
        AutomationProperties.SetHelpText(_renameBox, "Edit the selected object's name.");
        AutomationProperties.SetName(_renameButton, "Rename selected object");
        AutomationProperties.SetHelpText(_renameButton, "Apply the typed name to the selected object.");
        _renameButton.Click += (_, _) => RenameSelectedItem();
        AutomationProperties.SetName(_toggleVisibilityButton, "Toggle selected object visibility");
        AutomationProperties.SetHelpText(_toggleVisibilityButton, "Show or hide the selected object.");
        _toggleVisibilityButton.Click += (_, _) => ToggleSelectedVisibility();

        AutomationProperties.SetName(_moveUpButton, "Bring selected object forward");
        AutomationProperties.SetHelpText(_moveUpButton, "Move the selected object forward within its object type stack.");
        AutomationProperties.SetName(_moveDownButton, "Send selected object backward");
        AutomationProperties.SetHelpText(_moveDownButton, "Move the selected object backward within its object type stack.");
        _moveUpButton.Click += (_, _) => AcceptMove(SelectionPaneDialogAction.MoveUp);
        _moveDownButton.Click += (_, _) => AcceptMove(SelectionPaneDialogAction.MoveDown);
        AutomationProperties.SetName(_showAllButton, "Show all objects");
        AutomationProperties.SetHelpText(_showAllButton, "Mark every listed object visible.");
        AutomationProperties.SetName(_hideAllButton, "Hide all objects");
        AutomationProperties.SetHelpText(_hideAllButton, "Mark every listed object hidden.");
        _showAllButton.Click += (_, _) => SetAllVisibility(true);
        _hideAllButton.Click += (_, _) => SetAllVisibility(false);

        var okButton = new Button { Content = "_OK", Width = 78, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        AutomationProperties.SetName(okButton, "Apply selection pane changes");
        AutomationProperties.SetHelpText(okButton, "Apply visibility, rename, and stacking order changes.");
        okButton.Click += (_, _) => AcceptVisibility();
        var cancelButton = new Button { Content = "_Cancel", Width = 78, IsCancel = true };
        AutomationProperties.SetName(cancelButton, "Cancel selection pane changes");
        AutomationProperties.SetHelpText(cancelButton, "Close the selection pane without applying pending changes.");

        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        searchRow.Children.Add(new Label { Content = "_Search:", Target = _searchBox, Padding = new Thickness(0, 4, 6, 0) });
        searchRow.Children.Add(_searchBox);
        searchRow.Children.Add(new Label { Content = "_Filter:", Target = _filterBox, Padding = new Thickness(0, 4, 6, 0) });
        searchRow.Children.Add(_filterBox);

        var renameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        renameRow.Children.Add(new Label { Content = "_Name:", Target = _renameBox, Padding = new Thickness(0, 4, 6, 0) });
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

    private static DataTemplate CreateItemTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(FrameworkElement.MinHeightProperty, 24.0);

        var checkBox = new FrameworkElementFactory(typeof(CheckBox));
        checkBox.SetValue(CheckBox.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        checkBox.SetValue(FrameworkElement.WidthProperty, 24.0);
        checkBox.SetValue(CheckBox.ToolTipProperty, "Show or hide object");
        checkBox.SetValue(AutomationProperties.NameProperty, "Object visibility");
        checkBox.SetValue(AutomationProperties.HelpTextProperty, "Toggle whether this object is visible.");
        checkBox.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.IsVisible)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        panel.AppendChild(checkBox);

        var name = new FrameworkElementFactory(typeof(TextBox));
        name.SetValue(TextBox.MarginProperty, new Thickness(8, 0, 0, 0));
        name.SetValue(TextBox.WidthProperty, 160.0);
        name.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
        name.SetValue(TextBox.BackgroundProperty, Brushes.Transparent);
        name.SetValue(TextBox.ToolTipProperty, "Rename object");
        name.SetValue(AutomationProperties.NameProperty, "Object name");
        name.SetValue(AutomationProperties.HelpTextProperty, "Edit this object's display name.");
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

        return new DataTemplate { VisualTree = panel };
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
