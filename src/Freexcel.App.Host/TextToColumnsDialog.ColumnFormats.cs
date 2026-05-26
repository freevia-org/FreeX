using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public sealed partial class TextToColumnsDialog
{
    private GroupBox CreateColumnFormatPanel()
    {
        var root = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Label
        {
            Content = "_Column:",
            Target = _formatColumnBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 4, 8, 0)
        });
        row.Children.Add(_formatColumnBox);
        root.Children.Add(row);
        root.Children.Add(_formatGeneralButton);
        root.Children.Add(_formatTextButton);
        root.Children.Add(CreateDateFormatRow());
        root.Children.Add(_formatSkipButton);
        root.Children.Add(CreateAdvancedOptionsPanel());

        return new GroupBox
        {
            Header = "Column data format",
            Content = root,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 10, 0, 0)
        };
    }

    private StackPanel CreateDateFormatRow()
    {
        foreach (var value in DateColumnFormatLabels)
            _dateFormatBox.Items.Add(value);
        _dateFormatBox.SelectedIndex = 0;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_formatDateButton);
        row.Children.Add(_dateFormatBox);
        return row;
    }

    private GroupBox CreateAdvancedOptionsPanel()
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddAdvancedLabel(grid, "_Decimal separator:", _decimalSeparatorBox, 0, 0);
        AddAdvancedTextBox(grid, _decimalSeparatorBox, 0, 1);
        AddAdvancedLabel(grid, "_Thousands separator:", _thousandsSeparatorBox, 0, 2);
        AddAdvancedTextBox(grid, _thousandsSeparatorBox, 0, 3);
        Grid.SetRow(_trailingMinusBox, 1);
        Grid.SetColumnSpan(_trailingMinusBox, 4);
        _trailingMinusBox.Margin = new Thickness(0, 8, 0, 0);
        grid.Children.Add(_trailingMinusBox);

        return new GroupBox
        {
            Header = "Advanced",
            Content = grid,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 0)
        };
    }

    private static void AddAdvancedLabel(Grid grid, string content, Control target, int row, int column)
    {
        var label = new Label
        {
            Content = content,
            Target = target,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 4, 6, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private static void AddAdvancedTextBox(Grid grid, TextBox textBox, int row, int column)
    {
        textBox.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetRow(textBox, row);
        Grid.SetColumn(textBox, column);
        grid.Children.Add(textBox);
    }

    private TextToColumnsAdvancedOptions BuildAdvancedOptions()
    {
        TryParseAdvancedSeparator(_decimalSeparatorBox.Text, out var decimalSeparator);
        TryParseAdvancedSeparator(_thousandsSeparatorBox.Text, out var thousandsSeparator);
        return new(
            decimalSeparator,
            thousandsSeparator,
            _trailingMinusBox.IsChecked == true);
    }

    public static bool TryParseAdvancedSeparator(string? value, out string separator)
        => TextToColumnsDialogPlanner.TryParseAdvancedSeparator(value, out separator);

    private TextToColumnsTextQualifier SelectedTextQualifier() =>
        TextToColumnsDialogPlanner.TextQualifierFromSelectedIndex(_textQualifierBox.SelectedIndex);

    private void RefreshColumnFormatChoices(int columnCount)
    {
        var selectedIndex = Math.Max(0, _formatColumnBox.SelectedIndex);
        _suppressColumnFormatSync = true;
        try
        {
            _formatColumnBox.ItemsSource = Enumerable.Range(1, columnCount)
                .Select(index => $"Column {index}")
                .ToList();
            _formatColumnBox.SelectedIndex = Math.Min(selectedIndex, columnCount - 1);
        }
        finally
        {
            _suppressColumnFormatSync = false;
        }

        SyncColumnFormatControls();
    }

    private void SyncColumnFormatControls()
    {
        if (_suppressColumnFormatSync)
            return;

        var columnIndex = Math.Max(0, _formatColumnBox.SelectedIndex);
        var format = _columnFormats.TryGetValue(columnIndex, out var stored)
            ? stored
            : TextToColumnsColumnFormat.General;

        _suppressColumnFormatSync = true;
        try
        {
            _formatGeneralButton.IsChecked = format == TextToColumnsColumnFormat.General;
            _formatTextButton.IsChecked = format == TextToColumnsColumnFormat.Text;
            _formatDateButton.IsChecked = TextToColumnsDialogPlanner.IsDateColumnFormat(format);
            _dateFormatBox.SelectedItem = TextToColumnsDialogPlanner.DateColumnFormatLabel(format);
            _formatSkipButton.IsChecked = format == TextToColumnsColumnFormat.Skip;
        }
        finally
        {
            _suppressColumnFormatSync = false;
        }
    }

    private void StoreSelectedColumnFormat(TextToColumnsColumnFormat format)
    {
        if (_suppressColumnFormatSync || _formatColumnBox.SelectedIndex < 0)
            return;

        var columnIndex = _formatColumnBox.SelectedIndex;
        if (format == TextToColumnsColumnFormat.General)
            _columnFormats.Remove(columnIndex);
        else
            _columnFormats[columnIndex] = format;
    }

    private TextToColumnsColumnFormat SelectedDateColumnFormat() =>
        TextToColumnsDialogPlanner.DateColumnFormatFromLabel(_dateFormatBox.SelectedItem as string);

    private IReadOnlyList<TextToColumnsColumnFormat> BuildColumnFormats(int columnCount)
        => TextToColumnsDialogPlanner.BuildColumnFormats(columnCount, _columnFormats);
}
