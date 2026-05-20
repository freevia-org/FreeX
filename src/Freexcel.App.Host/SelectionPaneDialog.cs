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
    public string Name => Source.Name;
    public string Kind => Source.Kind.ToString();
    public bool IsVisible { get; set; } = item.IsVisible;
}

public sealed class SelectionPaneDialog : Window
{
    private readonly IReadOnlyList<SelectionPaneItem> _sourceItems;
    private readonly ListBox _list = new();
    private readonly Button _moveUpButton = new() { Content = "Bring Forward", Width = 104, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _moveDownButton = new() { Content = "Send Backward", Width = 104, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _showAllButton = new() { Content = "Show All", Width = 82, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _hideAllButton = new() { Content = "Hide All", Width = 82, Margin = new Thickness(0, 0, 6, 0) };

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
        _list.ItemsSource = items.Select(item => new SelectionPaneDialogItem(item)).ToList();
        _list.SelectionChanged += (_, _) => UpdateMoveButtons();
        if (_list.Items.Count > 0)
            _list.SelectedIndex = 0;
        _list.ItemTemplate = CreateItemTemplate();

        _moveUpButton.Click += (_, _) => AcceptMove(SelectionPaneDialogAction.MoveUp);
        _moveDownButton.Click += (_, _) => AcceptMove(SelectionPaneDialogAction.MoveDown);
        _showAllButton.Click += (_, _) => SetAllVisibility(true);
        _hideAllButton.Click += (_, _) => SetAllVisibility(false);

        var okButton = new Button { Content = "OK", Width = 78, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        okButton.Click += (_, _) => AcceptVisibility();
        var cancelButton = new Button { Content = "Cancel", Width = 78, IsCancel = true };

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
        stack.Children.Add(_list);
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
            _list.Items.Cast<SelectionPaneDialogItem>().Select(item => (item.Source.Id, item.IsVisible)).ToList());
        DialogResult = true;
    }

    private IReadOnlyList<SelectionPaneVisibilityChange> CurrentVisibilityChanges() =>
        CreateVisibilityChanges(
            _sourceItems,
            _list.Items.Cast<SelectionPaneDialogItem>().Select(item => (item.Source.Id, item.IsVisible)).ToList());

    private void SetAllVisibility(bool isVisible)
    {
        foreach (var item in _list.Items.Cast<SelectionPaneDialogItem>())
            item.IsVisible = isVisible;

        _list.Items.Refresh();
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
