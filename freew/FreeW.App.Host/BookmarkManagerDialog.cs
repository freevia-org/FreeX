using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A modal Bookmark Manager over the FreeW editing surface. Lists the document's bookmark targets (via
/// the pure <see cref="Bookmarks"/> helper, in document order) and offers two actions on the selected
/// entry: <b>Go To</b> scrolls/carets to the bookmarked paragraph (via
/// <see cref="DocumentView.BringBlockIntoView(int)"/>) and closes; <b>Delete</b> clears the bookmark
/// (via <see cref="Bookmarks.RemoveBookmark(TextDocument, string)"/>), re-renders, and refreshes the
/// list. View-only: it touches no docx I/O and changes no model shapes — only the existing
/// <see cref="Paragraph.BookmarkName"/> marker.
/// </summary>
internal sealed class BookmarkManagerDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private static readonly BookmarkManagerSurfaceSpec Surface = BookmarkManagerDialogPlanner.Surface;
    private readonly DocumentView _editor;
    private readonly ListBox _list = new() { MinWidth = Surface.ListMinWidth, MinHeight = Surface.ListMinHeight };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, Surface.StatusTopMargin, 0, 0) };
    private readonly Button _goToButton;
    private readonly Button _deleteButton;
    private readonly BookmarkManagerDialogSession _session = new();
    private bool _applyingState;

    private BookmarkManagerDialog(Window? owner, DocumentView editor)
    {
        _editor = editor;
        Owner = owner;
        Title = Surface.Title;
        AutomationProperties.SetAutomationId(this, Surface.WindowAutomationId);
        Width = Surface.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(Surface.OuterMargin) };
        var heading = new TextBlock { Text = Surface.Heading, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, Surface.HeadingBottomMargin) };
        AutomationProperties.SetAutomationId(heading, Surface.HeadingAutomationId);
        AutomationProperties.SetAutomationId(_list, Surface.ListAutomationId);
        AutomationProperties.SetAutomationId(_status, Surface.StatusAutomationId);
        panel.Children.Add(heading);

        _list.SelectionChanged += (_, _) => UpdateSelection();
        _list.MouseDoubleClick += (_, _) => GoTo();
        panel.Children.Add(_list);

        _goToButton = MakeButton(Surface.Action(BookmarkManagerActionKind.GoTo), (_, _) => GoTo());
        _deleteButton = MakeButton(Surface.Action(BookmarkManagerActionKind.Delete), (_, _) => Delete());
        var closeButton = MakeButton(Surface.Action(BookmarkManagerActionKind.Close), (_, _) => Close());

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, Surface.ActionTopMargin, 0, 0)
        };
        buttons.Children.Add(_goToButton);
        buttons.Children.Add(_deleteButton);
        buttons.Children.Add(closeButton);
        panel.Children.Add(buttons);

        panel.Children.Add(_status);
        Content = panel;

        Loaded += (_, _) =>
        {
            _list.Focus();
            Keyboard.Focus(_list);
        };

        RefreshList();
    }

    /// <summary>Opens the Bookmark Manager modally over <paramref name="owner"/>.</summary>
    public static void Show(Window? owner, DocumentView editor) =>
        new BookmarkManagerDialog(owner, editor).ShowDialog();

    private void RefreshList(BookmarkManagerDeleteRefreshPlan? deletePlan = null)
    {
        // Commit pending edits so the model reflects the current text before enumerating bookmarks.
        var locations = EnumerateBookmarks();
        var state = deletePlan is null
            ? _session.Refresh(locations)
            : _session.CompleteDelete(deletePlan, locations);
        ApplyState(state);
    }

    private IReadOnlyList<BookmarkLocation> EnumerateBookmarks()
    {
        _editor.CommitToModel();
        return Bookmarks.List(_editor.Model);
    }

    private void ApplyState(BookmarkManagerDialogState state)
    {
        _applyingState = true;
        try
        {
            _list.Items.Clear();
            foreach (var item in state.Items)
                _list.Items.Add(item);
            _list.SelectedIndex = state.SelectedIndex;
        }
        finally
        {
            _applyingState = false;
        }

        _status.Text = state.StatusText;
        ApplyActionState(state);
    }

    private void UpdateSelection()
    {
        if (_applyingState)
            return;

        ApplyActionState(_session.SelectIndex(_list.SelectedIndex));
    }

    private void ApplyActionState(BookmarkManagerDialogState state)
    {
        _goToButton.IsEnabled = state.IsEnabled(BookmarkManagerActionKind.GoTo);
        _deleteButton.IsEnabled = state.IsEnabled(BookmarkManagerActionKind.Delete);
    }

    private void GoTo()
    {
        var intent = _session.PlanGoTo();
        if (intent is null)
            return;
        _editor.BringBlockIntoView(intent.BlockIndex);
        Close();
    }

    private void Delete()
    {
        var plan = _session.PlanDelete();
        if (plan is null)
            return;

        // Removes the marker from the model and re-renders, then refresh the list.
        _editor.RemoveBookmark(plan.Name);
        RefreshList(plan);
    }

    private static Button MakeButton(BookmarkManagerActionSpec spec, RoutedEventHandler onClick)
    {
        var button = new Button { Content = spec.Label, IsCancel = spec.IsCancel, MinWidth = Surface.ButtonMinWidth };
        button.Margin = new Thickness(Surface.ButtonLeadingMargin, 0, 0, 0);
        button.Padding = new Thickness(Surface.ButtonHorizontalPadding, Surface.ButtonVerticalPadding, Surface.ButtonHorizontalPadding, Surface.ButtonVerticalPadding);
        AutomationProperties.SetAutomationId(button, spec.AutomationId);
        button.Click += onClick;
        return button;
    }
}
