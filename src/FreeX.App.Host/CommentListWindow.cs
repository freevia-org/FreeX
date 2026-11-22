using System.Windows;
using System.Windows.Automation;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// Floating window hosting a <see cref="CommentListView"/> (used for the Notes list). The threaded
/// Comments pane is docked in the main window instead; see MainWindow.CommentsPane.cs.
/// </summary>
public sealed class CommentListWindow : Window
{
    private readonly CommentListView _view;

    /// <param name="getPane">Shared Comments-pane view-model (filter + counts); null for the Notes list.</param>
    /// <param name="getCard">Shared card view-model for the selected thread (resolved state, permissions).</param>
    /// <param name="applyAction">Runs a card action (Resolve/Reopen/Delete thread) as an undoable command.</param>
    public CommentListWindow(
        string title,
        IEnumerable<CommentListRowPlan> items,
        Action<CellAddress> navigateTo,
        Func<ThreadedCommentPaneFilter, ThreadedCommentPanePlan>? getPane = null,
        Func<CellAddress, ThreadedCommentCardPlan?>? getCard = null,
        Action<CellAddress, ThreadedCommentCardAction>? applyAction = null)
    {
        Title = title;
        Width = 520;
        Height = 360;
        MinWidth = 380;
        MinHeight = 240;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        AutomationProperties.SetName(this, title);
        AutomationProperties.SetAutomationId(this, "ReviewCommentListWindow");

        _view = new CommentListView(title, items, navigateTo, getPane, getCard, applyAction, Close);
        Content = _view;
        Loaded += (_, _) => _view.FocusInitialItem();
    }

    public void Refresh(IEnumerable<CommentListRowPlan> items) => _view.Refresh(items);

    public static IReadOnlyList<CommentListRowPlan> CreateThreadedCommentItems(
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments) =>
        CommentNavigationPlanner.CreateThreadedCommentRows(threadedComments);

    public static IReadOnlyList<CommentListRowPlan> CreateNoteItems(IReadOnlyDictionary<CellAddress, string> notes) =>
        CommentNavigationPlanner.CreateNoteRows(notes);
}
