using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum DataTableMode
{
    OneVariable,
    TwoVariable
}

public sealed record DataTableDialogResult(
    DataTableMode Mode,
    CellAddress FormulaCell,
    CellAddress? RowInputCell,
    CellAddress? ColumnInputCell);

public sealed class DataTableDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly ComboBox _modeBox = new();
    private readonly TextBox _formulaBox = new();
    private readonly TextBox _rowInputBox = new();
    private readonly TextBox _columnInputBox = new();

    public DataTableDialogResult? Result { get; private set; }

    public DataTableDialog(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        Title = "Data Table";
        Width = 340;
        Height = 260;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _modeBox.ItemsSource = Enum.GetValues<DataTableMode>();
        _modeBox.SelectedItem = DataTableMode.OneVariable;
        _formulaBox.Text = new CellAddress(sheetId, range.Start.Row, Math.Min(CellAddress.MaxCol, range.Start.Col + 1)).ToA1();
        _columnInputBox.Text = _formulaBox.Text;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock { Text = "Type:" });
        root.Children.Add(_modeBox);
        root.Children.Add(new TextBlock { Text = "Formula cell:", Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(CreateReferenceEditor(_formulaBox, "Select formula cell"));
        root.Children.Add(new TextBlock { Text = "Row input cell:", Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(CreateReferenceEditor(_rowInputBox, "Select row input cell"));
        root.Children.Add(new TextBlock { Text = "Column input cell:", Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(CreateReferenceEditor(_columnInputBox, "Select column input cell"));
        root.Children.Add(TextToColumnsDialog.CreateButtonRow(Accept));
        Content = root;
    }

    public static bool TryParse(
        SheetId currentSheetId,
        DataTableMode mode,
        string formulaCellText,
        string? rowInputCellText,
        string? columnInputCellText,
        out DataTableDialogResult result,
        out string? error)
    {
        result = default!;
        error = null;

        if (!DataTableInputParser.TryParseCell(formulaCellText, currentSheetId, out var formulaCell))
        {
            error = "Enter a valid formula cell.";
            return false;
        }

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

        if (mode == DataTableMode.OneVariable && hasRowInput == hasColumnInput)
        {
            error = "Enter either a row input cell or a column input cell.";
            return false;
        }

        if (mode == DataTableMode.TwoVariable && (!hasRowInput || !hasColumnInput))
        {
            error = "Enter both row and column input cells.";
            return false;
        }

        result = new DataTableDialogResult(mode, formulaCell, rowInputCell, columnInputCell);
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

    private static DockPanel CreateReferenceEditor(TextBox textBox, string automationName)
    {
        var panel = new DockPanel();
        var pickerButton = new Button
        {
            Content = "...",
            Width = 28,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = textBox
        };
        AutomationProperties.SetName(pickerButton, automationName);
        pickerButton.Click += ReferencePickerButton_Click;
        panel.Children.Add(pickerButton);
        panel.Children.Add(textBox);
        return panel;
    }

    private static void ReferencePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TextBox textBox })
            return;

        textBox.Focus();
        textBox.SelectAll();
    }

    private void Accept()
    {
        if (!TryParse(_sheetId, (DataTableMode)_modeBox.SelectedItem, _formulaBox.Text, _rowInputBox.Text, _columnInputBox.Text, out var result, out var error))
        {
            MessageBox.Show(this, error ?? "Enter valid data table cells.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
