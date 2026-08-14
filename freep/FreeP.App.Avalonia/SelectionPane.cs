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
    private readonly PresentationSelectionPaneFormSession<Control> _formSession;
    private readonly StackPanel _items = new() { Orientation = Orientation.Vertical };
    private readonly TextBlock _message = new();

    public SelectionPane(EditingSession editor, Action? onAccessibilityChanged = null)
    {
        var session = new PresentationSelectionPaneSession(editor);
        _formSession = new(
            session,
            value => _message.Text = value,
            _items.Children.Clear,
            BuildItem,
            row => _items.Children.Add(row),
            plan => PresentationPaneAccessibilityAdapter.ApplyPaneMetadata(
                this,
                PresentationPaneAccessibilityPlanner.SelectionPaneId,
                IsVisible,
                plan.Items.Count,
                plan.SelectedItemIndex),
            onAccessibilityChanged);
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
            Text = _formSession.CurrentPlan.TitleText,
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
        _formSession.SetEditor(editor);
    }

    public PresentationSelectionPanePlan CurrentPlan => _formSession.CurrentPlan;

    public PresentationSelectionPanePlan Refresh() => _formSession.Refresh();

    private Control BuildItem(
        PresentationSelectionPaneItemPlan item,
        int index,
        PresentationSelectionPaneItemSession itemSession)
    {
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
        select.Click += (_, _) => _formSession.ApplyTransition(itemSession.Select());

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
            _formSession.ApplyTransition(
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
                _formSession.ApplyTransition(itemSession.CancelRename());
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
            _formSession.ApplyTransition(itemSession.ToggleVisibility());
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
            _formSession.ApplyTransition(itemSession.MoveTowardFront());

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
            _formSession.ApplyTransition(itemSession.MoveTowardBack());

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

    private static SolidColorBrush ToBrush(FreeP.Core.Model.SrgbColor color) =>
        new(Color.FromRgb(color.R, color.G, color.B));
}
