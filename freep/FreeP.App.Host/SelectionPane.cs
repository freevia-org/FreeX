using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

/// <summary>Small host adapter for the shared Selection Pane projection.</summary>
internal sealed class SelectionPane : Border
{
    private EditingSession _editor;
    private readonly Action? _onAccessibilityChanged;
    private readonly StackPanel _items = new();
    private readonly TextBlock _message = new();

    internal IReadOnlyList<FrameworkElement> AccessibilityItemsForTests =>
        _items.Children.OfType<FrameworkElement>().ToArray();

    public SelectionPane(EditingSession editor, Action? onAccessibilityChanged = null)
    {
        _editor = editor;
        _onAccessibilityChanged = onAccessibilityChanged;
        Width = 320;
        Visibility = Visibility.Collapsed;
        Background = Brushes.White;
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
        BorderThickness = new Thickness(1, 0, 0, 0);
        PresentationPaneAccessibilityAdapter.ApplyPaneMetadata(
            this,
            PresentationPaneAccessibilityPlanner.SelectionPaneId,
            isVisible: false);

        var heading = new TextBlock
        {
            Text = "Selection Pane",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _message.Margin = new Thickness(12, 0, 12, 8);
        _message.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

        var panel = new DockPanel();
        var header = new StackPanel();
        header.Children.Add(heading);
        header.Children.Add(_message);
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _items,
        });
        Child = panel;
    }

    public void SetEditor(EditingSession editor)
    {
        _editor = editor;
        Refresh();
    }

    public PresentationSelectionPanePlan Refresh()
    {
        var plan = PresentationSelectionPanePlanner.Build(
            _editor.CurrentSlide,
            _editor.CurrentSlideIndex,
            _editor.SelectedShapeIds);
        _message.Text = plan.HasSlide
            ? $"Slide {plan.SlideIndex + 1} ({plan.Items.Count} objects)"
            : PresentationSelectionPanePlanner.EmptyMessage;
        _items.Children.Clear();
        for (var index = 0; index < plan.Items.Count; index++)
            _items.Children.Add(BuildItem(plan.Items[index], index));
        PresentationPaneAccessibilityAdapter.ApplyPaneMetadata(
            this,
            PresentationPaneAccessibilityPlanner.SelectionPaneId,
            IsVisible,
            plan.Items.Count,
            Array.FindIndex(plan.Items.ToArray(), item => item.IsSelected));
        _onAccessibilityChanged?.Invoke();
        return plan;
    }

    private UIElement BuildItem(PresentationSelectionPaneItemPlan item, int index)
    {
        var select = new Button
        {
            Content = $"{item.SelectionIndex + 1}.",
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(8 + (item.NestingDepth * 16), 1, 4, 1),
            ToolTip = item.SelectToolTipText,
        };
        select.Click += (_, _) => _editor.Select(item.ShapeId);

        var rename = new TextBox
        {
            Text = item.ShapeName,
            MinWidth = 170,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 1, 4, 1),
            ToolTip = PresentationSelectionPaneItemPlan.RenameToolTipText,
        };
        var committed = false;
        void CommitName()
        {
            if (committed)
                return;
            committed = true;
            if (!_editor.SetShapeName(item.ShapeId, rename.Text))
                rename.Text = item.ShapeName;
        }
        rename.LostFocus += (_, _) => CommitName();
        rename.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                CommitName();
                args.Handled = true;
            }
            else if (args.Key == Key.Escape)
            {
                committed = true;
                Refresh();
                args.Handled = true;
            }
        };

        var visibility = new Button
        {
            Content = item.IsHidden ? "Show" : "Hide",
            MinWidth = 50,
            Padding = new Thickness(5, 3, 5, 3),
            Margin = new Thickness(0, 1, 8, 1),
            ToolTip = item.VisibilityToolTipText,
        };
        visibility.Click += (_, _) =>
        {
            if (_editor.ToggleShapeHidden(item.ShapeId))
                Refresh();
        };

        var moveUp = new Button
        {
            Content = "▲",
            Width = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 1, 2, 1),
            IsEnabled = item.CanMoveUp,
            ToolTip = PresentationSelectionPaneItemPlan.MoveUpToolTipText,
        };
        moveUp.Click += (_, _) => MoveItem(item, offset: 1);

        var moveDown = new Button
        {
            Content = "▼",
            Width = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 1, 2, 1),
            IsEnabled = item.CanMoveDown,
            ToolTip = PresentationSelectionPaneItemPlan.MoveDownToolTipText,
        };
        moveDown.Click += (_, _) => MoveItem(item, offset: -1);

        var row = new DockPanel();
        DockPanel.SetDock(visibility, Dock.Right);
        DockPanel.SetDock(moveDown, Dock.Right);
        DockPanel.SetDock(moveUp, Dock.Right);
        DockPanel.SetDock(rename, Dock.Right);
        row.Children.Add(visibility);
        row.Children.Add(moveDown);
        row.Children.Add(moveUp);
        row.Children.Add(rename);
        row.Children.Add(select);
        PresentationPaneAccessibilityAdapter.ApplyItem(
            row,
            PresentationPaneAccessibilityPlanner.SelectionPaneId,
            index,
            item.ShapeName,
            item.IsSelected ? "Selected" : "Not selected");
        return row;
    }

    private void MoveItem(PresentationSelectionPaneItemPlan item, int offset)
    {
        _editor.Select(item.ShapeId);
        if (_editor.MoveSelectedShapeInReadingOrder(offset))
            Refresh();
    }
}
