using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed class WatchWindowDialog : Window
{
    private readonly Func<IReadOnlyList<WatchWindowEntry>> _getEntries;
    private readonly Action? _addWatch;
    private readonly Func<string> _getSelectionText;
    private readonly Action<CellAddress> _navigateTo;
    private readonly Action<CellAddress> _removeWatch;
    private readonly ObservableCollection<WatchWindowRow> _rows = [];
    private readonly ListView _listView;

    public WatchWindowDialog(
        Func<IReadOnlyList<WatchWindowEntry>> getEntries,
        Action? addWatch,
        Func<string>? getSelectionText,
        Action<CellAddress> navigateTo,
        Action<CellAddress> removeWatch)
    {
        _getEntries = getEntries;
        _addWatch = addWatch;
        _getSelectionText = getSelectionText ?? (() => "");
        _navigateTo = navigateTo;
        _removeWatch = removeWatch;

        Title = "Watch Window";
        Width = 620;
        Height = 320;
        MinWidth = 480;
        MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

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

        var add = new Button
        {
            Content = "_Add Watch",
            Width = 96,
            Height = 26,
            Margin = new Thickness(4, 0, 0, 0),
            IsEnabled = _addWatch is not null,
            ToolTip = "Add the current worksheet selection to the Watch Window."
        };
        add.Click += (_, _) =>
        {
            var dialog = new AddWatchDialog(_getSelectionText()) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            _addWatch?.Invoke();
            Refresh();
        };
        buttons.Children.Add(add);

        var refresh = new Button { Content = "_Refresh", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        refresh.Click += (_, _) => Refresh();
        buttons.Children.Add(refresh);

        var delete = new Button { Content = "_Delete Watch", Width = 96, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        delete.Click += (_, _) => DeleteSelectedWatch();
        buttons.Children.Add(delete);

        var close = new Button { Content = "_Close", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        _listView = new ListView { ItemsSource = _rows, SelectionMode = SelectionMode.Extended };
        _listView.MouseDoubleClick += ListView_MouseDoubleClick;
        _listView.View = new System.Windows.Controls.GridView
        {
            Columns =
            {
                new GridViewColumn { Header = "Book", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRow.Book)) },
                new GridViewColumn { Header = "Sheet", Width = 110, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRow.Sheet)) },
                new GridViewColumn { Header = "Name", Width = 80, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRow.Name)) },
                new GridViewColumn { Header = "Cell", Width = 70, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRow.Cell)) },
                new GridViewColumn { Header = "Value", Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRow.Value)) },
                new GridViewColumn { Header = "Formula", Width = 170, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRow.Formula)) }
            }
        };
        root.Children.Add(_listView);

        Refresh();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public void Refresh()
    {
        _rows.Clear();
        foreach (var entry in _getEntries())
        {
            _rows.Add(new WatchWindowRow(
                "This Workbook",
                entry.SheetName,
                "",
                entry.Address.ToA1(),
                entry.ValueText,
                entry.FormulaText ?? "",
                entry.Address));
        }
    }

    private void DeleteSelectedWatch()
    {
        var selectedIndex = _listView.SelectedIndex;
        var fallbackAddress = (_listView.SelectedItem as WatchWindowRow)?.Address;
        var targets = WatchWindowService.GetDeleteTargets(
            _listView.SelectedItems.OfType<WatchWindowRow>().Select(row => row.Address),
            fallbackAddress);
        if (targets.Count == 0)
            return;

        foreach (var address in targets)
            _removeWatch(address);

        Refresh();
        if (_rows.Count > 0)
            _listView.SelectedIndex = Math.Min(Math.Max(0, selectedIndex), _rows.Count - 1);
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_listView.SelectedItem is WatchWindowRow row)
            _navigateTo(row.Address);
    }

    private void FocusInitialKeyboardTarget()
    {
        if (_rows.Count > 0 && _listView.SelectedIndex < 0)
            _listView.SelectedIndex = 0;

        _listView.Focus();
        Keyboard.Focus(_listView);
    }

    private sealed record WatchWindowRow(
        string Book,
        string Sheet,
        string Name,
        string Cell,
        string Value,
        string Formula,
        CellAddress Address);
}

public sealed class AddWatchDialog : Window
{
    private readonly TextBox _rangeBox = new();

    public AddWatchDialog(string selectedRangeText)
    {
        Title = "Add Watch";
        Width = 360;
        Height = 170;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(12) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var add = new Button { Content = "_Add", Width = 76, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(add);
        buttons.Children.Add(new Button { Content = "_Cancel", Width = 76, IsCancel = true });

        var body = new StackPanel();
        root.Children.Add(body);
        _rangeBox.Text = selectedRangeText;
        _rangeBox.IsReadOnly = true;
        _rangeBox.Margin = new Thickness(0, 0, 0, 8);
        body.Children.Add(new Label
        {
            Content = "Selected _range:",
            Target = _rangeBox,
            Padding = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        body.Children.Add(_rangeBox);
        body.Children.Add(new TextBlock
        {
            Text = "The selected cells will be added to the Watch Window.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush
        });

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        _rangeBox.Focus();
        _rangeBox.SelectAll();
        Keyboard.Focus(_rangeBox);
    }
}
