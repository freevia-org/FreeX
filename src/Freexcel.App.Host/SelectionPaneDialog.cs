using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum SelectionPaneDialogAction
{
    ApplyVisibility,
    MoveUp,
    MoveDown
}

public sealed record SelectionPaneVisibilityChange(SelectionPaneObjectKind Kind, Guid Id, bool IsVisible);

public sealed record SelectionPaneDialogResult(
    SelectionPaneDialogAction Action,
    SelectionPaneItem? Target,
    IReadOnlyList<SelectionPaneVisibilityChange> VisibilityChanges);

internal sealed class SelectionPaneDialogItem(SelectionPaneItem item)
{
    public SelectionPaneItem Source { get; } = item;
    public string Name { get; set; } = item.Name;
    public string Kind => Source.Kind.ToString();
    public bool IsVisible { get; set; } = item.IsVisible;
}

public sealed class SelectionPaneDialog : Window
{
    private readonly IReadOnlyList<SelectionPaneItem> _sourceItems;
    private readonly List<SelectionPaneDialogItem> _items;
    private readonly ListBox _list = new();
    private readonly TextBox _searchBox = new() { Width = 180, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _filterBox = new() { Width = 110, Margin = new Thickness(0, 0, 0, 0) };
    private readonly TextBox _renameBox = new() { Width = 180, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _renameButton = new() { Content = "_Rename", Width = 78, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _toggleVisibilityButton = new() { Content = "Eye", Width = 54, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Toggle visibility" };
    private readonly Button _moveUpButton = new() { Content = "_Bring Forward", Width = 104, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _moveDownButton = new() { Content = "Send _Backward", Width = 104, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _showAllButton = new() { Content = "Show _All", Width = 82, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _hideAllButton = new() { Content = "_Hide All", Width = 82, Margin = new Thickness(0, 0, 6, 0) };

    public SelectionPaneDialogResult Result { get; private set; }

    public SelectionPaneDialog(IReadOnlyList<SelectionPaneItem> items)
    {
        _sourceItems = items;
        Result = new SelectionPaneDialogResult(SelectionPaneDialogAction.ApplyVisibility, null, []);
        Title = "Selection Pane";
        Width = 380;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _list.Margin = new Thickness(0, 0, 0, 10);
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
        _searchBox.TextChanged += (_, _) => ApplySearchAndFilter();
        _filterBox.ItemsSource = new[] { "All", "Visible", "Hidden", "Charts", "Pictures", "Shapes", "Text Boxes" };
        _filterBox.SelectedIndex = 0;
        _filterBox.SelectionChanged += (_, _) => ApplySearchAndFilter();
        _renameButton.Click += (_, _) => RenameSelectedItem();
        _toggleVisibilityButton.Click += (_, _) => ToggleSelectedVisibility();

        _moveUpButton.Click += (_, _) => AcceptMove(SelectionPaneDialogAction.MoveUp);
        _moveDownButton.Click += (_, _) => AcceptMove(SelectionPaneDialogAction.MoveDown);
        _showAllButton.Click += (_, _) => SetAllVisibility(true);
        _hideAllButton.Click += (_, _) => SetAllVisibility(false);

        var okButton = new Button { Content = "_OK", Width = 78, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        okButton.Click += (_, _) => AcceptVisibility();
        var cancelButton = new Button { Content = "_Cancel", Width = 78, IsCancel = true };

        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        searchRow.Children.Add(new Label { Content = "_Search:", Target = _searchBox, Padding = new Thickness(0, 4, 6, 0) });
        searchRow.Children.Add(_searchBox);
        searchRow.Children.Add(new Label { Content = "_Filter:", Target = _filterBox, Padding = new Thickness(0, 4, 6, 0) });
        searchRow.Children.Add(_filterBox);

        var renameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
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
    }

    public static IReadOnlyList<SelectionPaneVisibilityChange> CreateVisibilityChanges(
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible)> currentStates)
    {
        var states = currentStates.ToDictionary(state => state.Id, state => state.IsVisible);
        return originalItems
            .Where(item => states.TryGetValue(item.Id, out var isVisible) && isVisible != item.IsVisible)
            .Select(item => new SelectionPaneVisibilityChange(item.Kind, item.Id, states[item.Id]))
            .ToList();
    }

    public static SelectionPaneDialogResult CreateResult(
        SelectionPaneDialogAction action,
        SelectionPaneItem? target,
        IReadOnlyList<SelectionPaneItem> originalItems,
        IReadOnlyList<(Guid Id, bool IsVisible)> currentStates) =>
        new(
            action,
            target,
            CreateVisibilityChanges(originalItems, currentStates));

    private static DataTemplate CreateItemTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var checkBox = new FrameworkElementFactory(typeof(CheckBox));
        checkBox.SetValue(CheckBox.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        checkBox.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.IsVisible)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        panel.AppendChild(checkBox);

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.Name)));
        panel.AppendChild(name);

        var kind = new FrameworkElementFactory(typeof(TextBlock));
        kind.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));
        kind.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Gray);
        kind.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.Kind)));
        panel.AppendChild(kind);

        return new DataTemplate { VisualTree = panel };
    }

    private void AcceptVisibility()
    {
        Result = new SelectionPaneDialogResult(
            SelectionPaneDialogAction.ApplyVisibility,
            null,
            CurrentVisibilityChanges());
        DialogResult = true;
    }

    private void AcceptMove(SelectionPaneDialogAction action)
    {
        if (_list.SelectedItem is not SelectionPaneDialogItem selected)
            return;

        Result = CreateResult(
            action,
            selected.Source,
            _sourceItems,
            _items.Select(item => (item.Source.Id, item.IsVisible)).ToList());
        DialogResult = true;
    }

    private IReadOnlyList<SelectionPaneVisibilityChange> CurrentVisibilityChanges() =>
        CreateVisibilityChanges(
            _sourceItems,
            _items.Select(item => (item.Source.Id, item.IsVisible)).ToList());

    private void SetAllVisibility(bool isVisible)
    {
        foreach (var item in _items)
            item.IsVisible = isVisible;

        _list.Items.Refresh();
    }

    private void ApplySearchAndFilter()
    {
        var search = _searchBox.Text.Trim();
        var filter = _filterBox.SelectedItem as string ?? "All";
        var filtered = _items.Where(item =>
            MatchesSearch(item, search) &&
            MatchesFilter(item, filter)).ToList();

        _list.ItemsSource = filtered;
        if (_list.SelectedIndex < 0 && _list.Items.Count > 0)
            _list.SelectedIndex = 0;
        UpdateMoveButtons();
        UpdateRenameBox();
    }

    private static bool MatchesSearch(SelectionPaneDialogItem item, string search) =>
        string.IsNullOrWhiteSpace(search) ||
        item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        item.Kind.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesFilter(SelectionPaneDialogItem item, string filter) =>
        filter switch
        {
            "Visible" => item.IsVisible,
            "Hidden" => !item.IsVisible,
            "Charts" => item.Source.Kind == SelectionPaneObjectKind.Chart,
            "Pictures" => item.Source.Kind == SelectionPaneObjectKind.Picture,
            "Shapes" => item.Source.Kind == SelectionPaneObjectKind.Shape,
            "Text Boxes" => item.Source.Kind == SelectionPaneObjectKind.TextBox,
            _ => true
        };

    private void RenameSelectedItem()
    {
        if (_list.SelectedItem is not SelectionPaneDialogItem selected)
            return;

        var name = _renameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        selected.Name = name;
        _list.Items.Refresh();
    }

    private void ToggleSelectedVisibility()
    {
        if (_list.SelectedItem is not SelectionPaneDialogItem selected)
            return;

        selected.IsVisible = !selected.IsVisible;
        _list.Items.Refresh();
    }

    private void UpdateRenameBox()
    {
        if (_list.SelectedItem is SelectionPaneDialogItem selected)
            _renameBox.Text = selected.Name;
    }

    private void UpdateMoveButtons()
    {
        if (_list.SelectedItem is not SelectionPaneDialogItem selected)
        {
            _moveUpButton.IsEnabled = false;
            _moveDownButton.IsEnabled = false;
            return;
        }

        _moveUpButton.IsEnabled = selected.Source.CanMoveUp;
        _moveDownButton.IsEnabled = selected.Source.CanMoveDown;
    }
}
