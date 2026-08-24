using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Panes;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>Layout > Arrange selection pane for selecting and reordering floating objects.</summary>
public sealed class SelectionPane : SidePaneBase
{
    private readonly ListBox _items;
    private readonly Button _bringForward;
    private readonly Button _sendBackward;

    public SelectionPane(DocumentView editor)
        : base(editor, "Selection Pane", 240, new Thickness(1, 0, 0, 0), includeSeparator: true)
    {
        _items = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        _items.SelectionChanged += OnSelectionChanged;

        _bringForward = new Button { Content = "Bring Forward", Margin = new Thickness(8, 4, 4, 6) };
        _bringForward.Click += (_, _) => MoveSelected(ZOrderOperation.BringForward);
        _sendBackward = new Button { Content = "Send Backward", Margin = new Thickness(4, 4, 8, 6) };
        _sendBackward.Click += (_, _) => MoveSelected(ZOrderOperation.SendBackward);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        commands.Children.Add(_bringForward);
        commands.Children.Add(_sendBackward);
        DockPanel.SetDock(commands, Dock.Bottom);
        InnerLayout.Children.Add(commands);
        InnerLayout.Children.Add(_items);
        UpdateButtons();
    }

    public override void Refresh()
    {
        _items.SelectionChanged -= OnSelectionChanged;
        _items.Items.Clear();
        foreach (var item in SelectionPaneProjection.Build(_editor.Document))
            _items.Items.Add(item);
        _items.SelectionChanged += OnSelectionChanged;
        UpdateButtons();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_items.SelectedItem is SelectionPaneItem item)
            _editor.SelectFloating(item.BlockIndex, item.RunIndex);
        UpdateButtons();
    }

    private void MoveSelected(ZOrderOperation operation)
    {
        if (_editor.ChangeSelectedFloatingZOrder(operation))
            Refresh();
    }

    private void UpdateButtons()
    {
        var enabled = _items.SelectedItem is SelectionPaneItem;
        _bringForward.IsEnabled = enabled;
        _sendBackward.IsEnabled = enabled;
    }
}
