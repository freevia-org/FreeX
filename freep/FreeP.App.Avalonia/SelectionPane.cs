using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

/// <summary>Small host adapter for the shared Selection Pane projection.</summary>
internal sealed class SelectionPane : Border
{
    private readonly PresentationSelectionPaneSession _session;
    private readonly StackPanel _items = new() { Orientation = Orientation.Vertical };
    private readonly TextBlock _message = new();

    internal IReadOnlyList<Control> AccessibilityItemsForTests => _items.Children.OfType<Control>().ToArray();

    public SelectionPane(EditingSession editor)
    {
        _session = new PresentationSelectionPaneSession(editor);
        Width = 320;
        IsVisible = false;
        Background = Brushes.White;
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
        BorderThickness = new Thickness(1, 0, 0, 0);
        PresentationPaneAccessibilityAdapter.ApplyPaneMetadata(
            this,
            PresentationPaneAccessibilityPlanner.SelectionPaneId,
            isVisible: false);

        var heading = new TextBlock
        {
            Text = _session.CurrentPlan.TitleText,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _message.Margin = new Thickness(12, 0, 12, 8);
        _message.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

        var header = new StackPanel { Children = { heading, _message } };
        var panel = new DockPanel();
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
        Render(_session.SetEditor(editor));
    }

    public PresentationSelectionPanePlan CurrentPlan => _session.CurrentPlan;

    public PresentationSelectionPanePlan Refresh() => Render(_session.Refresh());

    private PresentationSelectionPanePlan Render(PresentationSelectionPanePlan plan)
    {
        _message.Text = plan.StatusText;
        _items.Children.Clear();
        for (var index = 0; index < plan.Items.Count; index++)
            _items.Children.Add(BuildItem(plan.Items[index], index));
        PresentationPaneAccessibilityAdapter.ApplyPaneMetadata(
            this,
            PresentationPaneAccessibilityPlanner.SelectionPaneId,
            IsVisible,
            plan.Items.Count,
            plan.SelectedItemIndex);
        return plan;
    }

    private Control BuildItem(PresentationSelectionPaneItemPlan item, int index)
    {
        var itemSession = _session.CreateItemSession(item.ShapeId);
        var select = new Button
        {
            Content = item.SelectText,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 5),
            Margin = new Thickness(8 + (item.NestingDepth * 16), 1, 4, 1),
        };
        ToolTip.SetTip(select, item.SelectToolTipText);
        select.Click += (_, _) => ApplyTransition(itemSession.Select());

        var rename = new TextBox
        {
            Text = item.ShapeName,
            MinWidth = 170,
            Padding = new Thickness(4, 3),
            Margin = new Thickness(0, 1, 4, 1),
        };
        ToolTip.SetTip(rename, PresentationSelectionPaneItemPlan.RenameToolTipText);
        void CommitName()
        {
            ApplyTransition(
                itemSession.CommitRename(rename.Text),
                restoreName => rename.Text = restoreName);
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
                ApplyTransition(itemSession.CancelRename());
                args.Handled = true;
            }
        };

        var visibility = new Button
        {
            Content = item.VisibilityActionText,
            MinWidth = 50,
            Padding = new Thickness(5, 3),
            Margin = new Thickness(0, 1, 8, 1),
        };
        ToolTip.SetTip(visibility, item.VisibilityToolTipText);
        visibility.Click += (_, _) =>
        {
            ApplyTransition(itemSession.ToggleVisibility());
        };

        var moveUp = new Button
        {
            Content = item.MoveUpText,
            Width = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 1, 2, 1),
            IsEnabled = item.CanMoveUp,
        };
        ToolTip.SetTip(moveUp, PresentationSelectionPaneItemPlan.MoveUpToolTipText);
        moveUp.Click += (_, _) =>
            ApplyTransition(itemSession.MoveTowardFront());

        var moveDown = new Button
        {
            Content = item.MoveDownText,
            Width = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 1, 2, 1),
            IsEnabled = item.CanMoveDown,
        };
        ToolTip.SetTip(moveDown, PresentationSelectionPaneItemPlan.MoveDownToolTipText);
        moveDown.Click += (_, _) =>
            ApplyTransition(itemSession.MoveTowardBack());

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
            item.AccessibilityStateText);
        return row;
    }

    internal IReadOnlyList<string?> RenameToolTipsForTests =>
        _items.Children
            .OfType<DockPanel>()
            .Select(row => row.Children.OfType<TextBox>().SingleOrDefault())
            .Select(textBox => textBox is null ? null : ToolTip.GetTip(textBox)?.ToString())
            .ToArray();

    private void ApplyTransition(
        PresentationSelectionPaneTransitionPlan transition,
        Action<string>? restoreName = null)
    {
        if (transition.RestoreNameText is { } name)
            restoreName?.Invoke(name);
        if (transition.ShouldRefreshPane)
            Render(transition.PanePlan);
    }
}
