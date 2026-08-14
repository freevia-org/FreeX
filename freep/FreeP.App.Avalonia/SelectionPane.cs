using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

/// <summary>Small host adapter for the shared Selection Pane projection.</summary>
internal sealed partial class SelectionPane : Border
{
    private readonly PresentationSelectionPaneSession _session;
    private readonly Action? _onAccessibilityChanged;
    private readonly StackPanel _items = new() { Orientation = Orientation.Vertical };
    private readonly TextBlock _message = new();

    public SelectionPane(EditingSession editor, Action? onAccessibilityChanged = null)
    {
        _session = new PresentationSelectionPaneSession(editor);
        _onAccessibilityChanged = onAccessibilityChanged;
        Width = PresentationSelectionPaneVisualMetrics.PaneWidth;
        IsVisible = false;
        Background = ToBrush(PresentationSelectionPaneVisualMetrics.PaneBackgroundColor);
        BorderBrush = ToBrush(PresentationSelectionPaneVisualMetrics.PaneBorderColor);
        BorderThickness = new Thickness(
            PresentationSelectionPaneVisualMetrics.PaneBorderThickness,
            0,
            0,
            0);
        PresentationPaneAccessibilityAdapter.ApplyPaneMetadata(
            this,
            PresentationPaneAccessibilityPlanner.SelectionPaneId,
            isVisible: false);

        var heading = new TextBlock
        {
            Text = _session.CurrentPlan.TitleText,
            FontSize = PresentationSelectionPaneVisualMetrics.HeadingFontSize,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(
                PresentationSelectionPaneVisualMetrics.ContentSideMargin,
                PresentationSelectionPaneVisualMetrics.HeadingTopMargin,
                PresentationSelectionPaneVisualMetrics.ContentSideMargin,
                PresentationSelectionPaneVisualMetrics.HeadingBottomMargin),
        };
        _message.Margin = new Thickness(
            PresentationSelectionPaneVisualMetrics.ContentSideMargin,
            0,
            PresentationSelectionPaneVisualMetrics.ContentSideMargin,
            PresentationSelectionPaneVisualMetrics.MessageBottomMargin);
        _message.Foreground = ToBrush(PresentationSelectionPaneVisualMetrics.MessageColor);

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
        _onAccessibilityChanged?.Invoke();
        return plan;
    }

    private Control BuildItem(PresentationSelectionPaneItemPlan item, int index)
    {
        var itemSession = _session.CreateItemSession(item.ShapeId);
        var select = new Button
        {
            Content = item.SelectText,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(
                PresentationSelectionPaneVisualMetrics.SelectHorizontalPadding,
                PresentationSelectionPaneVisualMetrics.SelectVerticalPadding),
            Margin = new Thickness(
                PresentationSelectionPaneVisualMetrics.SelectHorizontalPadding +
                    (item.NestingDepth * PresentationSelectionPaneVisualMetrics.NestingIndent),
                PresentationSelectionPaneVisualMetrics.ItemVerticalMargin,
                PresentationSelectionPaneVisualMetrics.SelectRightMargin,
                PresentationSelectionPaneVisualMetrics.ItemVerticalMargin),
        };
        ToolTip.SetTip(select, item.SelectToolTipText);
        select.Click += (_, _) => ApplyTransition(itemSession.Select());

        var rename = new TextBox
        {
            Text = item.ShapeName,
            MinWidth = PresentationSelectionPaneVisualMetrics.RenameMinimumWidth,
            Padding = new Thickness(
                PresentationSelectionPaneVisualMetrics.FieldHorizontalPadding,
                PresentationSelectionPaneVisualMetrics.FieldVerticalPadding),
            Margin = new Thickness(
                0,
                PresentationSelectionPaneVisualMetrics.ItemVerticalMargin,
                PresentationSelectionPaneVisualMetrics.RenameRightMargin,
                PresentationSelectionPaneVisualMetrics.ItemVerticalMargin),
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
            MinWidth = PresentationSelectionPaneVisualMetrics.VisibilityMinimumWidth,
            Padding = new Thickness(
                PresentationSelectionPaneVisualMetrics.VisibilityHorizontalPadding,
                PresentationSelectionPaneVisualMetrics.VisibilityVerticalPadding),
            Margin = new Thickness(
                0,
                PresentationSelectionPaneVisualMetrics.ItemVerticalMargin,
                PresentationSelectionPaneVisualMetrics.VisibilityRightMargin,
                PresentationSelectionPaneVisualMetrics.ItemVerticalMargin),
        };
        ToolTip.SetTip(visibility, item.VisibilityToolTipText);
        visibility.Click += (_, _) =>
        {
            ApplyTransition(itemSession.ToggleVisibility());
        };

        var moveUp = new Button
        {
            Content = item.MoveUpText,
            Width = PresentationSelectionPaneVisualMetrics.MoveButtonWidth,
            Padding = new Thickness(0),
            Margin = new Thickness(
                0,
                PresentationSelectionPaneVisualMetrics.ItemVerticalMargin,
                PresentationSelectionPaneVisualMetrics.MoveButtonRightMargin,
                PresentationSelectionPaneVisualMetrics.ItemVerticalMargin),
            IsEnabled = item.CanMoveUp,
        };
        ToolTip.SetTip(moveUp, PresentationSelectionPaneItemPlan.MoveUpToolTipText);
        moveUp.Click += (_, _) =>
            ApplyTransition(itemSession.MoveTowardFront());

        var moveDown = new Button
        {
            Content = item.MoveDownText,
            Width = PresentationSelectionPaneVisualMetrics.MoveButtonWidth,
            Padding = new Thickness(0),
            Margin = new Thickness(
                0,
                PresentationSelectionPaneVisualMetrics.ItemVerticalMargin,
                PresentationSelectionPaneVisualMetrics.MoveButtonRightMargin,
                PresentationSelectionPaneVisualMetrics.ItemVerticalMargin),
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
            PresentationPaneAccessibilityPlanner.PlanItem(
                PresentationPaneAccessibilityPlanner.SelectionPaneId,
                index,
                item.ShapeName,
                item.IsSelected,
                PresentationPaneAccessibilityPlanner.BuildShapeKey(item.ShapeId)));
        return row;
    }

    private void ApplyTransition(
        PresentationSelectionPaneTransitionPlan transition,
        Action<string>? restoreName = null)
    {
        if (transition.RestoreNameText is { } name)
            restoreName?.Invoke(name);
        if (transition.ShouldRefreshPane)
            Render(transition.PanePlan);
    }

    private static SolidColorBrush ToBrush(FreeP.Core.Model.SrgbColor color) =>
        new(Color.FromRgb(color.R, color.G, color.B));
}
