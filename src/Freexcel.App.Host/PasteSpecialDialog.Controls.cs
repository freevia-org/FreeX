using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public sealed partial class PasteSpecialDialog
{
    private GroupBox CreatePasteGroup()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 8; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddPasteChoice(grid, _rbAll, 0, 0);
        AddPasteChoice(grid, _rbValues, 1, 0);
        AddPasteChoice(grid, _rbFormulas, 2, 0);
        AddPasteChoice(grid, _rbFormats, 3, 0);
        AddPasteChoice(grid, _rbComments, 4, 0);
        AddPasteChoice(grid, _rbValidation, 5, 0);
        AddPasteChoice(grid, _rbColumnWidths, 6, 0);
        AddPasteChoice(grid, _rbAllUsingSourceTheme, 0, 1);
        AddPasteChoice(grid, _rbAllExceptBorders, 1, 1);
        AddPasteChoice(grid, _rbAllMergingConditionalFormats, 2, 1);
        AddPasteChoice(grid, _rbFormulasAndNumberFormats, 3, 1);
        AddPasteChoice(grid, _rbValuesAndNumberFormats, 4, 1);
        AddPasteChoice(grid, _rbValuesAndSourceFormatting, 5, 1);
        AddPasteChoice(grid, _rbPicture, 6, 1);
        AddPasteChoice(grid, _rbLinkedPicture, 7, 1);

        return new GroupBox
        {
            Header = "Paste",
            Content = grid,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 10)
        };
    }

    private StackPanel CreatePasteOptionsPanel()
    {
        var options = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        options.Children.Add(_skipBlanks);
        options.Children.Add(_transpose);
        options.Children.Add(_keepColumnWidths);
        return options;
    }

    private GroupBox CreateOperationGroup() =>
        new()
        {
            Header = "Operation",
            Content = CreateOperationPanel(),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 12)
        };

    private StackPanel CreateFooterRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _pasteLinkButton.Click += (_, _) =>
        {
            _pasteLinkRequested = true;
            DialogResult = true;
        };

        var ok = new Button { Content = "_OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "_Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => { DialogResult = true; };
        row.Children.Add(_pasteLinkButton);
        row.Children.Add(ok);
        row.Children.Add(cancel);
        return row;
    }

    private static void AddPasteChoice(Grid panel, RadioButton button, int row, int column)
    {
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        panel.Children.Add(button);
    }

    private static RadioButton CreateOperationButton(string content, bool isChecked = false) =>
        new()
        {
            Content = content,
            GroupName = "PasteSpecialOperation",
            IsChecked = isChecked,
            Margin = new Thickness(0, 0, 12, 6)
        };

    private Grid CreateOperationPanel()
    {
        var panel = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddOperation(panel, _opNone, 0, 0);
        AddOperation(panel, _opAdd, 0, 1);
        AddOperation(panel, _opSubtract, 1, 0);
        AddOperation(panel, _opMultiply, 1, 1);
        AddOperation(panel, _opDivide, 2, 0);
        return panel;
    }

    private static void AddOperation(Grid panel, RadioButton button, int row, int column)
    {
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        panel.Children.Add(button);
    }
}
