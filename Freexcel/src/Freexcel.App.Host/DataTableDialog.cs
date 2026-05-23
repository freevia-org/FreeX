using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum DataTableMode
{
    OneVariable,
    TwoVariable
}

public sealed record DataTableDialogResult(
    DataTableMode Mode,
    DataTableInputOrientation Orientation,
    CellAddress FormulaCell,
    CellAddress? RowInputCell,
    CellAddress? ColumnInputCell);

public sealed class DataTableDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly TextBox _rowInputBox = new();
    private readonly TextBox _columnInputBox = new();

    public DataTableDialogResult? Result { get; private set; }

    public DataTableDialog(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
        Title = "Data Table";
        Width = 360;
        Height = 210;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddReferenceRow(grid, 0, "_Row input cell:", _rowInputBox, "Select row input cell");
        AddReferenceRow(grid, 1, "_Column input cell:", _columnInputBox, "Select column input cell");
        root.Children.Add(grid);
        root.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 76));
        Content = root;
    }

    public static bool TryParse(
        SheetId currentSheetId,
        GridRange range,
        string? rowInputCellText,
        string? columnInputCellText,
        out DataTableDialogResult result,
        out string? error)
    {
        result = default!;
        error = null;

        var hasRowInput = !string.IsNullOrWhiteSpace(rowInputCellText);
        var hasColumnInput = !string.IsNullOrWhiteSpace(columnInputCellText);
        if (!TryParseOptionalCell(currentSheetId, rowInputCellText, hasRowInput, out var rowInputCell))
        {
            error = "Enter a valid row input cell.";
            return false;
        }

        if (!TryParseOptionalCell(currentSheetId, columnInputCellText, hasColumnInput, out var columnInputCell))
        {
            error = "Enter a valid column input cell.";
            return false;
        }

        if (!hasRowInput && !hasColumnInput)
        {
            error = "Enter either a row input cell or a column input cell.";
            return false;
        }

        var mode = hasRowInput && hasColumnInput ? DataTableMode.TwoVariable : DataTableMode.OneVariable;
        var orientation = hasRowInput && !hasColumnInput
            ? DataTableInputOrientation.Row
            : DataTableInputOrientation.Column;
        var formulaCell = DataTableInputParser.GetDefaultFormulaCell(range, orientation, mode == DataTableMode.TwoVariable);

        result = new DataTableDialogResult(mode, orientation, formulaCell, rowInputCell, columnInputCell);
        return true;
    }

    private static bool TryParseOptionalCell(
        SheetId sheetId,
        string? text,
        bool shouldParse,
        out CellAddress? address)
    {
        address = null;
        if (!shouldParse)
            return true;

        if (!DataTableInputParser.TryParseCell(text!, sheetId, out var parsed))
            return false;

        address = parsed;
        return true;
    }

    private static DockPanel CreateReferenceEditor(TextBox textBox, string automationName) =>
        DialogReferencePicker.CreateEditor(textBox, automationName, new Thickness(6, 0, 0, 0), Dock.Right);

    private static void AddReferenceRow(Grid grid, int row, string label, TextBox textBox, string automationName)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelBlock = new Label
        {
            Content = label,
            Target = textBox,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(0, row == 0 ? 0 : 8, 8, 0)
        };
        Grid.SetRow(labelBlock, row);
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);

        var editor = CreateReferenceEditor(textBox, automationName);
        editor.Margin = new Thickness(0, row == 0 ? 0 : 8, 0, 0);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
    }

    private void Accept()
    {
        if (!TryParse(_sheetId, _range, _rowInputBox.Text, _columnInputBox.Text, out var result, out var error))
        {
            MessageBox.Show(this, error ?? "Enter valid data table cells.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
