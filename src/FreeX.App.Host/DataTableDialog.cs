using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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

public enum DataTableRangeSelectionTarget
{
    RowInputCell,
    ColumnInputCell
}

public sealed record DataTableRangeSelectionRequest(
    DataTableRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);

public sealed class DataTableDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly TextBox _rowInputBox = new();
    private readonly TextBox _columnInputBox = new();
    private readonly Action<DataTableRangeSelectionRequest>? _requestRangeSelection;

    public DataTableDialogResult? Result { get; private set; }
    public DataTableRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public DataTableDialog(
        SheetId sheetId,
        GridRange range,
        Action<DataTableRangeSelectionRequest>? requestRangeSelection = null)
    {
        _sheetId = sheetId;
        _range = range;
        _requestRangeSelection = requestRangeSelection;
        Title = UiText.Get("DataTable_Title");
        Width = 360;
        Height = 210;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        AutomationProperties.SetName(_rowInputBox, UiText.Get("DataTable_RowInputAutomationName"));
        AutomationProperties.SetName(_columnInputBox, UiText.Get("DataTable_ColumnInputAutomationName"));
        AutomationProperties.SetAutomationId(_rowInputBox, "DataTableRowInputCellBox");
        AutomationProperties.SetHelpText(_rowInputBox, UiText.Get("DataTable_RowInputAutomationHelpText"));
        AutomationProperties.SetAutomationId(_columnInputBox, "DataTableColumnInputCellBox");
        AutomationProperties.SetHelpText(_columnInputBox, UiText.Get("DataTable_ColumnInputAutomationHelpText"));

        var root = new StackPanel { Margin = new Thickness(12) };
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddReferenceRow(
            grid,
            0,
            UiText.Get("DataTable_RowInputLabel"),
            _rowInputBox,
            UiText.Get("DataTable_RowInputPickerAutomationName"),
            DataTableRangeSelectionTarget.RowInputCell);
        AddReferenceRow(
            grid,
            1,
            UiText.Get("DataTable_ColumnInputLabel"),
            _columnInputBox,
            UiText.Get("DataTable_ColumnInputPickerAutomationName"),
            DataTableRangeSelectionTarget.ColumnInputCell);
        root.Children.Add(grid);
        root.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 76));
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
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
            error = UiText.Get("DataTable_InvalidRowInputMessage");
            return false;
        }

        if (!TryParseOptionalCell(currentSheetId, columnInputCellText, hasColumnInput, out var columnInputCell))
        {
            error = UiText.Get("DataTable_InvalidColumnInputMessage");
            return false;
        }

        if (!hasRowInput && !hasColumnInput)
        {
            error = UiText.Get("DataTable_MissingInputMessage");
            return false;
        }

        if (rowInputCell is { } rowCell && range.Contains(rowCell))
        {
            error = UiText.Get("DataTable_RowInputInsideRangeMessage");
            return false;
        }

        if (columnInputCell is { } columnCell && range.Contains(columnCell))
        {
            error = UiText.Get("DataTable_ColumnInputInsideRangeMessage");
            return false;
        }

        if (rowInputCell is { } rowInput && columnInputCell is { } columnInput && rowInput == columnInput)
        {
            error = UiText.Get("DataTable_SameInputCellMessage");
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

    public static DataTableRangeSelectionRequest CreateRangeSelectionRequest(
        DataTableRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

    private DockPanel CreateReferenceEditor(
        TextBox textBox,
        string automationName,
        DataTableRangeSelectionTarget target) =>
        DialogReferencePicker.CreateEditor(
            textBox,
            automationName,
            new Thickness(6, 0, 0, 0),
            Dock.Right,
            request => RequestRangeSelection(target, request));

    private void AddReferenceRow(
        Grid grid,
        int row,
        string label,
        TextBox textBox,
        string automationName,
        DataTableRangeSelectionTarget target)
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

        var editor = CreateReferenceEditor(textBox, automationName, target);
        editor.Margin = new Thickness(0, row == 0 ? 0 : 8, 0, 0);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
    }

    private void RequestRangeSelection(DataTableRangeSelectionTarget target, DialogReferencePickerRequest request)
    {
        RangeSelectionRequest = CreateRangeSelectionRequest(target, request.CurrentText);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        FocusRangeSelectionInput(request.Target);
    }

    public void ApplyRangeSelection(DataTableRangeSelectionTarget target, CellAddress address)
    {
        var textBox = target == DataTableRangeSelectionTarget.ColumnInputCell
            ? _columnInputBox
            : _rowInputBox;
        textBox.Text = address.ToA1();
        FocusRangeSelectionInput(textBox);
    }

    private static void FocusRangeSelectionInput(TextBox target)
    {
        DialogFocus.FocusAndSelect(target);
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusRangeSelectionInput(_rowInputBox);
    }

    private void FocusInvalidInput(string? error)
    {
        var target = string.Equals(error, UiText.Get("DataTable_InvalidColumnInputMessage"), StringComparison.Ordinal) ||
            string.Equals(error, UiText.Get("DataTable_ColumnInputInsideRangeMessage"), StringComparison.Ordinal) ||
            string.Equals(error, UiText.Get("DataTable_SameInputCellMessage"), StringComparison.Ordinal)
            ? _columnInputBox
            : _rowInputBox;
        DialogFocus.FocusAndSelect(target);
    }

    private void Accept()
    {
        if (!TryParse(_sheetId, _range, _rowInputBox.Text, _columnInputBox.Text, out var result, out var error))
        {
            DialogMessageHelper.ShowWarning(this, error ?? UiText.Get("DataTable_InvalidCellsMessage"), Title);
            FocusInvalidInput(error);
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
