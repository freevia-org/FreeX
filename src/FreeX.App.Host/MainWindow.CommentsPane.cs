using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private CommentListView? _commentsPaneView;
    private double? _commentsPaneUserWidth;

    private bool IsCommentsPaneOpen => _commentsPaneView is not null && CommentsPane.Visibility == Visibility.Visible;

    /// <summary>
    /// Opens (or refreshes) the docked threaded Comments pane: a real layout column right of the grid,
    /// so the grid shrinks instead of being covered. Resizable with its left-edge splitter and closed
    /// with its X button or by pressing Show Comments again.
    /// </summary>
    private void ShowCommentsPane(IReadOnlyList<CommentListRowPlan> items)
    {
        // Both docked panes share the right-hand layout column; opening one replaces the other.
        if (IsChartFormatPaneOpen)
            CloseChartFormatPane();

        if (_commentsPaneView is null)
        {
            _commentsPaneView = new CommentListView(
                UiText.Get("MainWindowMessage_CommentsTitle"),
                items,
                NavigateToCell,
                getPane: filter => ThreadedCommentCardPlanner.BuildPane(CurrentCommentSheet(), filter),
                getCard: address => ThreadedCommentCardPlanner.BuildCard(CurrentCommentSheet(), address),
                applyAction: ApplyThreadedCommentCardActionFromPane);
            CommentsPane.Child = BuildCommentsPaneChrome(_commentsPaneView);
            CommentsPane.Width = ThreadedCommentActivationPlanner.ClampPaneWidth(
                _commentsPaneUserWidth ?? ThreadedCommentActivationPlanner.DefaultPaneWidth(WorkbookWorkAreaRoot.ActualWidth),
                WorkbookWorkAreaRoot.ActualWidth);
            WorkbookWorkAreaRoot.SizeChanged += CommentsPaneHost_SizeChanged;
        }
        else
        {
            _commentsPaneView.Refresh(items);
        }

        CommentsPane.Visibility = Visibility.Visible;
        _commentsPaneView.FocusInitialItem();
    }

    private void CloseCommentsPane(bool restoreGridFocus = true)
    {
        if (_commentsPaneView is null)
            return;

        WorkbookWorkAreaRoot.SizeChanged -= CommentsPaneHost_SizeChanged;
        _commentsPaneView = null;
        CommentsPane.Child = null;
        CommentsPane.Visibility = Visibility.Collapsed;
        if (restoreGridFocus)
            SheetGrid.Focus();
    }

    private void CommentsPaneHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsCommentsPaneOpen)
            return;

        CommentsPane.Width = ThreadedCommentActivationPlanner.ClampPaneWidth(
            CommentsPane.Width, WorkbookWorkAreaRoot.ActualWidth);
    }

    private UIElement BuildCommentsPaneChrome(CommentListView view)
    {
        var title = UiText.Get("MainWindowMessage_CommentsTitle");

        var closeButton = new Button
        {
            Content = "X",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        AutomationProperties.SetName(closeButton, UiText.Get("ReviewCommentList_CloseButtonAutomationName"));
        AutomationProperties.SetAutomationId(closeButton, "ReviewCommentsPaneCloseButton");
        closeButton.Click += (_, _) => CloseCommentsPane();

        var header = new DockPanel { Margin = new Thickness(10, 8, 10, 0), LastChildFill = true };
        DockPanel.SetDock(closeButton, Dock.Right);
        header.Children.Add(closeButton);
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });

        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        content.Children.Add(header);
        content.Children.Add(view);

        var splitter = new Thumb
        {
            Width = 6,
            Cursor = Cursors.SizeWE,
            Background = System.Windows.Media.Brushes.Transparent,
            Template = BuildCommentsPaneSplitterTemplate()
        };
        AutomationProperties.SetAutomationId(splitter, "ReviewCommentsPaneResizeGrip");
        AutomationProperties.SetName(splitter, title);
        splitter.DragDelta += (_, e) =>
        {
            // The pane is docked on the right: dragging left (negative) widens it.
            var requested = CommentsPane.Width - e.HorizontalChange;
            CommentsPane.Width = ThreadedCommentActivationPlanner.ClampPaneWidth(
                requested, WorkbookWorkAreaRoot.ActualWidth);
            _commentsPaneUserWidth = CommentsPane.Width;
        };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(splitter, 0);
        Grid.SetColumn(content, 1);
        root.Children.Add(splitter);
        root.Children.Add(content);
        return root;
    }

    private static ControlTemplate BuildCommentsPaneSplitterTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = border };
    }
}
