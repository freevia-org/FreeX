using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class CommentListWindow : Window
{
    private readonly Action<CellAddress> _navigateTo;
    private readonly ObservableCollection<CommentListRowPlan> _items = [];
    private readonly ListView _listView = new();
    private readonly Button _openButton = new();
    private readonly Func<ThreadedCommentPaneFilter, ThreadedCommentPanePlan>? _getPane;
    private readonly Func<CellAddress, ThreadedCommentCardPlan?>? _getCard;
    private readonly Action<CellAddress, ThreadedCommentCardAction>? _applyAction;
    private readonly ComboBox _filterBox = new() { MinWidth = 110 };
    private readonly TextBlock _summaryText = new() { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _toggleResolvedButton = new();
    private readonly Button _deleteThreadButton = new();
    private IReadOnlyList<CommentListRowPlan> _allItems = [];

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
        _navigateTo = navigateTo;
        _getPane = getPane;
        _getCard = getCard;
        _applyAction = applyAction;

        Title = title;
        Width = 520;
        Height = 360;
        MinWidth = 380;
        MinHeight = 240;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        AutomationProperties.SetName(this, title);
        AutomationProperties.SetAutomationId(this, "ReviewCommentListWindow");

        var root = new DockPanel { Margin = new Thickness(10) };
        Content = root;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _openButton.Content = UiText.Get("ReviewCommentList_OpenButton");
        _openButton.Width = 80;
        _openButton.Height = 26;
        _openButton.Margin = new Thickness(4, 0, 0, 0);
        AutomationProperties.SetName(_openButton, UiText.Get("ReviewCommentList_OpenButtonAutomationName"));
        AutomationProperties.SetAutomationId(_openButton, "ReviewCommentListOpenButton");
        AutomationProperties.SetHelpText(_openButton, UiText.Get("ReviewCommentList_OpenButtonHelpText"));
        _openButton.Click += (_, _) => OpenSelectedItem();
        buttons.Children.Add(_openButton);

        if (_getPane is not null)
        {
            _toggleResolvedButton.Content = UiText.Get("ThreadedCommentCard_ResolveThread");
            _toggleResolvedButton.MinWidth = 112;
            _toggleResolvedButton.Height = 26;
            _toggleResolvedButton.Margin = new Thickness(4, 0, 0, 0);
            AutomationProperties.SetAutomationId(_toggleResolvedButton, "ReviewCommentListToggleResolvedButton");
            _toggleResolvedButton.Click += (_, _) => ApplySelectedThreadAction(delete: false);
            buttons.Children.Insert(0, _toggleResolvedButton);

            _deleteThreadButton.Content = UiText.Get("ThreadedCommentCard_DeleteThread");
            _deleteThreadButton.MinWidth = 104;
            _deleteThreadButton.Height = 26;
            _deleteThreadButton.Margin = new Thickness(4, 0, 0, 0);
            AutomationProperties.SetAutomationId(_deleteThreadButton, "ReviewCommentListDeleteThreadButton");
            _deleteThreadButton.Click += (_, _) => ApplySelectedThreadAction(delete: true);
            buttons.Children.Insert(1, _deleteThreadButton);

            _filterBox.Items.Add(UiText.Get("ThreadedCommentPane_FilterAll"));
            _filterBox.Items.Add(UiText.Get("ThreadedCommentPane_FilterActive"));
            _filterBox.Items.Add(UiText.Get("ThreadedCommentPane_FilterResolved"));
            _filterBox.SelectedIndex = 0;
            AutomationProperties.SetAutomationId(_filterBox, "ReviewCommentListFilter");
            AutomationProperties.SetName(_filterBox, title);
            _filterBox.SelectionChanged += (_, _) => Refresh(_allItems);
            AutomationProperties.SetAutomationId(_summaryText, "ReviewCommentListSummary");
            var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            filterRow.Children.Add(_filterBox);
            filterRow.Children.Add(_summaryText);
            DockPanel.SetDock(filterRow, Dock.Top);
            root.Children.Add(filterRow);
        }

        var closeButton = new Button
        {
            Content = UiText.Get("ReviewCommentList_CloseButton"),
            Width = 80,
            Height = 26,
            Margin = new Thickness(4, 0, 0, 0),
            IsCancel = true
        };
        AutomationProperties.SetName(closeButton, UiText.Get("ReviewCommentList_CloseButtonAutomationName"));
        AutomationProperties.SetAutomationId(closeButton, "ReviewCommentListCloseButton");
        closeButton.Click += (_, _) => Close();
        buttons.Children.Add(closeButton);

        _listView.ItemsSource = _items;
        _listView.SelectionMode = SelectionMode.Single;
        _listView.MouseDoubleClick += (_, _) => OpenSelectedItem();
        _listView.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return)
            {
                OpenSelectedItem();
                e.Handled = true;
            }
        };
        _listView.SelectionChanged += (_, _) => UpdateOpenButtonState();
        _listView.View = new System.Windows.Controls.GridView
        {
            Columns =
            {
                new GridViewColumn
                {
                    Header = UiText.Get("ReviewCommentList_CellColumnHeader"),
                    Width = 80,
                    DisplayMemberBinding = new System.Windows.Data.Binding(nameof(CommentListRowPlan.Cell))
                },
                new GridViewColumn
                {
                    Header = UiText.Get("ReviewCommentList_TextColumnHeader"),
                    Width = 390,
                    DisplayMemberBinding = new System.Windows.Data.Binding(nameof(CommentListRowPlan.Text))
                }
            }
        };
        AutomationProperties.SetName(_listView, title);
        AutomationProperties.SetAutomationId(_listView, "ReviewCommentList");
        AutomationProperties.SetHelpText(_listView, UiText.Get("ReviewCommentList_ListHelpText"));
        root.Children.Add(_listView);

        Refresh(items);
        Loaded += (_, _) => FocusInitialItem();
    }

    public void Refresh(IEnumerable<CommentListRowPlan> items)
    {
        var selectedAddress = (_listView.SelectedItem as CommentListRowPlan)?.Address;
        _allItems = items as IReadOnlyList<CommentListRowPlan> ?? items.ToList();
        IEnumerable<CommentListRowPlan> visible = _allItems;
        if (_getPane is not null)
        {
            var pane = _getPane(CurrentFilter());
            var addresses = pane.Rows.Select(row => row.Address).ToHashSet();
            visible = _allItems.Where(item => addresses.Contains(item.Address));
            _summaryText.Text = UiText.Format("ThreadedCommentPane_SummaryFormat", pane.ActiveCount, pane.ResolvedCount);
        }

        _items.Clear();
        foreach (var item in visible)
            _items.Add(item);

        if (selectedAddress is { } address)
        {
            foreach (var item in _items.Where(item => item.Address.Equals(address)))
            {
                _listView.SelectedItem = item;
                break;
            }
        }

        if (_listView.SelectedItem is null && _items.Count > 0)
            _listView.SelectedIndex = 0;

        UpdateOpenButtonState();
    }

    public static IReadOnlyList<CommentListRowPlan> CreateThreadedCommentItems(
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments) =>
        CommentNavigationPlanner.CreateThreadedCommentRows(threadedComments);

    public static IReadOnlyList<CommentListRowPlan> CreateNoteItems(IReadOnlyDictionary<CellAddress, string> notes) =>
        CommentNavigationPlanner.CreateNoteRows(notes);

    private void FocusInitialItem()
    {
        _listView.Focus();
        if (_listView.SelectedItem is null && _items.Count > 0)
            _listView.SelectedIndex = 0;
    }

    private void OpenSelectedItem()
    {
        if (_listView.SelectedItem is not CommentListRowPlan item)
            return;

        _navigateTo(item.Address);
    }

    private void UpdateOpenButtonState()
    {
        _openButton.IsEnabled = _listView.SelectedItem is CommentListRowPlan;
        UpdateThreadButtonState();
    }

    private ThreadedCommentPaneFilter CurrentFilter() => _filterBox.SelectedIndex switch
    {
        1 => ThreadedCommentPaneFilter.Active,
        2 => ThreadedCommentPaneFilter.Resolved,
        _ => ThreadedCommentPaneFilter.All
    };

    private void UpdateThreadButtonState()
    {
        if (_getPane is null)
            return;

        var card = _listView.SelectedItem is CommentListRowPlan item ? _getCard?.Invoke(item.Address) : null;
        _toggleResolvedButton.IsEnabled = card?.CanToggleResolved == true;
        _deleteThreadButton.IsEnabled = card?.CanDeleteThread == true;
        if (card is not null)
            _toggleResolvedButton.Content = card.ToggleResolvedLabel.Resolve(UiText.Get, UiText.Format);
    }

    private void ApplySelectedThreadAction(bool delete)
    {
        if (_listView.SelectedItem is not CommentListRowPlan item || _applyAction is null)
            return;

        var card = _getCard?.Invoke(item.Address);
        if (card is null)
            return;

        _applyAction(
            item.Address,
            delete ? ThreadedCommentCardAction.DeleteThread : card.ToggleResolvedAction);
    }
}
