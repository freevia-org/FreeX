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

    public CommentListWindow(string title, IEnumerable<CommentListRowPlan> items, Action<CellAddress> navigateTo)
    {
        _navigateTo = navigateTo;

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
        _items.Clear();
        foreach (var item in items)
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
    }
}
