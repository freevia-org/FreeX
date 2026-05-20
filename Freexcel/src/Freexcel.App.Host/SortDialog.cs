using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public sealed record SortDialogLevel(uint ColumnOffset, bool Ascending);

public sealed class SortDialog : Window
{
    private readonly ObservableCollection<SortDialogLevel> _levels;

    public IReadOnlyList<SortDialogLevel> Levels => _levels.ToList();

    public IReadOnlyList<SortKey> ResultSortKeys { get; private set; }

    public SortDialog(IEnumerable<SortDialogLevel>? levels = null)
    {
        _levels = new ObservableCollection<SortDialogLevel>(NormalizeLevels(levels));
        ResultSortKeys = BuildSortKeys(_levels);

        Title = "Sort";
        Width = 420;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { Margin = new Thickness(16) };
        var list = new ListBox
        {
            ItemsSource = _levels,
            DisplayMemberPath = nameof(SortDialogLevel),
            Margin = new Thickness(0, 0, 0, 12)
        };
        list.ItemTemplate = CreateLevelTemplate();
        DockPanel.SetDock(list, Dock.Top);
        root.Children.Add(list);

        var helperRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 16)
        };
        var add = new Button { Content = "_Add Level", Width = 98, Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) => _levels.Add(new SortDialogLevel(0, true));
        var remove = new Button { Content = "_Remove Level", Width = 116 };
        remove.Click += (_, _) =>
        {
            var selectedIndex = list.SelectedIndex < 0 ? _levels.Count - 1 : list.SelectedIndex;
            var updated = RemoveLevel(_levels, selectedIndex);
            _levels.Clear();
            foreach (var level in updated)
                _levels.Add(level);
        };
        helperRow.Children.Add(add);
        helperRow.Children.Add(remove);
        DockPanel.SetDock(helperRow, Dock.Bottom);
        root.Children.Add(helperRow);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var ok = new Button { Content = "_OK", IsDefault = true, Width = 76, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) =>
        {
            ResultSortKeys = BuildSortKeys(_levels);
            DialogResult = true;
        };
        var cancel = new Button { Content = "_Cancel", IsCancel = true, Width = 76 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        Content = root;
    }

    public static IReadOnlyList<SortKey> BuildSortKeys(IEnumerable<SortDialogLevel> levels)
    {
        return NormalizeLevels(levels)
            .Select(level => new SortKey(level.ColumnOffset, level.Ascending))
            .ToList();
    }

    public static IReadOnlyList<SortDialogLevel> AddLevel(
        IEnumerable<SortDialogLevel> levels,
        uint columnOffset = 0,
        bool ascending = true)
    {
        return NormalizeLevels(levels)
            .Append(new SortDialogLevel(columnOffset, ascending))
            .ToList();
    }

    public static IReadOnlyList<SortDialogLevel> RemoveLevel(IEnumerable<SortDialogLevel> levels, int index)
    {
        var updated = NormalizeLevels(levels).ToList();
        if (index >= 0 && index < updated.Count)
            updated.RemoveAt(index);

        return updated.Count == 0 ? [new SortDialogLevel(0, true)] : updated;
    }

    private static IReadOnlyList<SortDialogLevel> NormalizeLevels(IEnumerable<SortDialogLevel>? levels)
    {
        var normalized = levels?.ToList() ?? [];
        return normalized.Count == 0 ? [new SortDialogLevel(0, true)] : normalized;
    }

    private static DataTemplate CreateLevelTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var column = new FrameworkElementFactory(typeof(TextBlock));
        column.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SortDialogLevel.ColumnOffset))
        {
            StringFormat = "Column offset {0}"
        });
        column.SetValue(FrameworkElement.WidthProperty, 150d);
        panel.AppendChild(column);

        var direction = new FrameworkElementFactory(typeof(TextBlock));
        direction.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SortDialogLevel.Ascending))
        {
            Converter = new SortDirectionTextConverter()
        });
        panel.AppendChild(direction);

        return new DataTemplate { VisualTree = panel };
    }
}

internal sealed class SortDirectionTextConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is bool ascending && !ascending ? "Descending" : "Ascending";
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is string text && text.Equals("Descending", StringComparison.OrdinalIgnoreCase) ? false : true;
    }
}
