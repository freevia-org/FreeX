using System;
using System.Windows;
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
    private readonly DocumentView _editor;
    private readonly ListBox _list = new() { MinWidth = 300, MinHeight = 180 };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _goToButton;
    private readonly Button _deleteButton;
    private readonly BookmarkManagerDialogSession _session = new();
    private bool _applyingState;

    private BookmarkManagerDialog(Window? owner, DocumentView editor)
    {
        _editor = editor;
        Owner = owner;
        Title = "Bookmark Manager";
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "BookmarkManagerDialog");
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = "Bookmarks:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        System.Windows.Automation.AutomationProperties.SetAutomationId(panel.Children[0], "BookmarkManagerHeading");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_list, "BookmarkManagerList");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_status, "BookmarkManagerStatus");

        _list.SelectionChanged += (_, _) => UpdateSelection();
        _list.MouseDoubleClick += (_, _) => GoTo();
        panel.Children.Add(_list);

        _goToButton = MakeButton("Go To", (_, _) => GoTo());
        _deleteButton = MakeButton("Delete", (_, _) => Delete());
        var closeButton = MakeButton("Close", (_, _) => Close());
        System.Windows.Automation.AutomationProperties.SetAutomationId(_goToButton, "BookmarkManagerGoToButton");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_deleteButton, "BookmarkManagerDeleteButton");
        System.Windows.Automation.AutomationProperties.SetAutomationId(closeButton, "BookmarkManagerCloseButton");
        closeButton.IsCancel = true;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
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
        _goToButton.IsEnabled = state.CanGoTo;
        _deleteButton.IsEnabled = state.CanDelete;
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

    private static Button MakeButton(string content, RoutedEventHandler onClick)
    {
        var button = new Button { Content = content, MinWidth = 84, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 3, 6, 3) };
        button.Click += onClick;
        return button;
    }
}
