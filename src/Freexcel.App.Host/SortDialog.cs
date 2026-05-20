using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record SortColumnChoice(string Label, uint ColumnOffset);

public sealed record SortDirectionChoice(string Label, bool Ascending);

public sealed class SortDialogLevel : IEquatable<SortDialogLevel>
{
    public SortDialogLevel(uint columnOffset, bool ascending)
    {
        ColumnOffset = columnOffset;
        Ascending = ascending;
    }

    public uint ColumnOffset { get; set; }

    public bool Ascending { get; set; }

    public bool Equals(SortDialogLevel? other) =>
        other is not null &&
        ColumnOffset == other.ColumnOffset &&
        Ascending == other.Ascending;

    public override bool Equals(object? obj) => Equals(obj as SortDialogLevel);

    public override int GetHashCode() => HashCode.Combine(ColumnOffset, Ascending);

    public override string ToString() => $"Column offset {ColumnOffset}, {(Ascending ? "Ascending" : "Descending")}";
}

public sealed class SortDialog : Window
{
    private static readonly IReadOnlyList<SortDirectionChoice> DirectionChoices =
    [
        new("A to Z", true),
        new("Z to A", false)
    ];

    private readonly ObservableCollection<SortDialogLevel> _levels;
    private readonly IReadOnlyList<SortColumnChoice> _columnChoices;

    public IReadOnlyList<SortDialogLevel> Levels => _levels.ToList();

    public IReadOnlyList<SortKey> ResultSortKeys { get; private set; }

    public SortDialog(
        IEnumerable<SortDialogLevel>? levels = null,
        IEnumerable<SortColumnChoice>? columnChoices = null)
    {
        _levels = new ObservableCollection<SortDialogLevel>(NormalizeLevels(levels));
        _columnChoices = NormalizeColumnChoices(columnChoices);
        ResultSortKeys = BuildSortKeys(_levels);

        Title = "Sort";
        Width = 500;
        Height = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { Margin = new Thickness(16) };
        var list = new DataGrid
        {
            ItemsSource = _levels,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            Margin = new Thickness(0, 0, 0, 12)
        };
        list.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Sort by",
            ItemsSource = _columnChoices,
            DisplayMemberPath = nameof(SortColumnChoice.Label),
            SelectedValuePath = nameof(SortColumnChoice.ColumnOffset),
            SelectedValueBinding = new Binding(nameof(SortDialogLevel.ColumnOffset))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        list.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Order",
            ItemsSource = DirectionChoices,
            DisplayMemberPath = nameof(SortDirectionChoice.Label),
            SelectedValuePath = nameof(SortDirectionChoice.Ascending),
            SelectedValueBinding = new Binding(nameof(SortDialogLevel.Ascending))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(140)
        });
        DockPanel.SetDock(list, Dock.Top);
        root.Children.Add(list);

        var helperRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
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
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
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

    public static IReadOnlyList<SortDialogLevel> UpdateLevel(
        IEnumerable<SortDialogLevel> levels,
        int index,
        uint columnOffset,
        bool ascending)
    {
        var updated = NormalizeLevels(levels).ToList();
        if (index >= 0 && index < updated.Count)
            updated[index] = new SortDialogLevel(columnOffset, ascending);

        return updated;
    }

    public static IReadOnlyList<SortColumnChoice> BuildColumnChoices(GridRange range)
    {
        var choices = new List<SortColumnChoice>();
        for (uint offset = 0; offset < range.ColCount; offset++)
        {
            var columnName = CellAddress.NumberToColumnName(range.Start.Col + offset);
            choices.Add(new SortColumnChoice($"Column {columnName}", offset));
        }

        return choices.Count == 0 ? [new SortColumnChoice("Column A", 0)] : choices;
    }

    private static IReadOnlyList<SortDialogLevel> NormalizeLevels(IEnumerable<SortDialogLevel>? levels)
    {
        var normalized = levels?.ToList() ?? [];
        return normalized.Count == 0 ? [new SortDialogLevel(0, true)] : normalized;
    }

    private static IReadOnlyList<SortColumnChoice> NormalizeColumnChoices(IEnumerable<SortColumnChoice>? choices)
    {
        var normalized = choices?.ToList() ?? [];
        return normalized.Count == 0 ? [new SortColumnChoice("Column A", 0)] : normalized;
    }
}
