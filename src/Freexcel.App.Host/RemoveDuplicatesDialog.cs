using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record RemoveDuplicateColumnChoice(uint Offset, string Header, bool IsSelected);

public sealed record RemoveDuplicatesDialogResult(IReadOnlyList<uint> SelectedColumnOffsets);

public sealed class RemoveDuplicatesDialog : Window
{
    private readonly List<CheckBox> _boxes = [];

    public RemoveDuplicatesDialogResult? Result { get; private set; }

    public RemoveDuplicatesDialog(IEnumerable<RemoveDuplicateColumnChoice> columns)
    {
        Title = "Remove Duplicates";
        Width = 320;
        Height = 320;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        foreach (var column in columns)
        {
            var box = new CheckBox
            {
                Content = column.Header,
                Tag = column.Offset,
                IsChecked = column.IsSelected,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _boxes.Add(box);
            root.Children.Add(box);
        }
        root.Children.Add(TextToColumnsDialog.CreateButtonRow(Accept));
        Content = root;
    }

    public static IReadOnlyList<RemoveDuplicateColumnChoice> SelectAll(int columnCount) =>
        BuildColumnChoices(columnCount, isSelected: true);

    public static IReadOnlyList<RemoveDuplicateColumnChoice> SelectAll(IEnumerable<RemoveDuplicateColumnChoice> columns) =>
        columns.Select(column => column with { IsSelected = true }).ToList();

    public static IReadOnlyList<RemoveDuplicateColumnChoice> ClearAll(IEnumerable<RemoveDuplicateColumnChoice> columns) =>
        columns.Select(column => column with { IsSelected = false }).ToList();

    public static RemoveDuplicatesDialogResult CreateResult(IEnumerable<RemoveDuplicateColumnChoice> columns)
    {
        var offsets = columns
            .Where(column => column.IsSelected)
            .Select(column => column.Offset)
            .ToList();
        return new RemoveDuplicatesDialogResult(offsets);
    }

    private static IReadOnlyList<RemoveDuplicateColumnChoice> BuildColumnChoices(int columnCount, bool isSelected)
    {
        if (columnCount < 0)
            throw new ArgumentOutOfRangeException(nameof(columnCount), columnCount, "Column count cannot be negative.");

        return Enumerable
            .Range(0, columnCount)
            .Select(index => new RemoveDuplicateColumnChoice((uint)index, $"Column {index + 1}", isSelected))
            .ToList();
    }

    public static IReadOnlyList<RemoveDuplicateColumnChoice> BuildColumnChoices(GridRange range) =>
        Enumerable
            .Range(0, (int)range.ColCount)
            .Select(index => new RemoveDuplicateColumnChoice((uint)index, $"Column {index + 1}", true))
            .ToList();

    public static IReadOnlyList<RemoveDuplicateColumnChoice> BuildColumnChoices(Sheet sheet, GridRange range) =>
        Enumerable
            .Range(0, (int)range.ColCount)
            .Select(index =>
            {
                var absoluteColumn = range.Start.Col + (uint)index;
                var header = SpreadsheetDisplayFormatter.FormatCellValue(sheet.GetCell(range.Start.Row, absoluteColumn)?.Value);
                if (string.IsNullOrWhiteSpace(header))
                    header = $"Column {CellAddress.NumberToColumnName(absoluteColumn)}";

                return new RemoveDuplicateColumnChoice((uint)index, header, true);
            })
            .ToList();

    private void Accept()
    {
        Result = CreateResult(_boxes.Select(box => new RemoveDuplicateColumnChoice(
            (uint)box.Tag,
            box.Content?.ToString() ?? "",
            box.IsChecked == true)));
        if (Result.SelectedColumnOffsets.Count == 0)
        {
            MessageBox.Show(this, "Select at least one column.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
