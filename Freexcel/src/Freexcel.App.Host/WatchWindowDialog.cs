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
    private readonly Action<CellAddress> _navigateTo;
    private readonly Action<CellAddress> _removeWatch;
    private readonly ObservableCollection<WatchWindowRow> _rows = [];
    private readonly ListView _listView;

    public WatchWindowDialog(
        Func<IReadOnlyList<WatchWindowEntry>> getEntries,
        Action<CellAddress> navigateTo,
        Action<CellAddress> removeWatch)
    {
        _getEntries = getEntries;
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
                new GridViewColumn { Header = "Cell", Width = 70, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRow.Cell)) },
                new GridViewColumn { Header = "Value", Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRow.Value)) },
                new GridViewColumn { Header = "Formula", Width = 190, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(WatchWindowRow.Formula)) }
            }
        };
        root.Children.Add(_listView);

        Refresh();
    }

    public void Refresh()
    {
        _rows.Clear();
        foreach (var entry in _getEntries())
        {
            _rows.Add(new WatchWindowRow(
                "This Workbook",
                entry.SheetName,
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

    private sealed record WatchWindowRow(
        string Book,
        string Sheet,
        string Cell,
        string Value,
        string Formula,
        CellAddress Address);
}
