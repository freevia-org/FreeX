using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record CreateTableDialogResult(GridRange Range, bool FirstRowHasHeaders, string TableStyleName);
public sealed record CreateTableRangeSelectionRequest(string CurrentText, bool CollapseDialog = true);

public sealed class CreateTableDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _rangeBox = new();
    private readonly CheckBox _headersBox = new() { Content = "_My table has headers", IsChecked = true };
    private readonly string _tableStyleName;
    private readonly Action<CreateTableRangeSelectionRequest>? _requestRangeSelection;

    public CreateTableDialogResult? Result { get; private set; }
    public CreateTableRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public CreateTableDialog(
        SheetId sheetId,
        string defaultRangeText,
        string tableStyleName,
        Action<CreateTableRangeSelectionRequest>? requestRangeSelection = null)
    {
        _sheetId = sheetId;
        _tableStyleName = tableStyleName;
        _requestRangeSelection = requestRangeSelection;
        Title = "Create Table";
        Width = 360;
        Height = 190;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _rangeBox.Text = defaultRangeText;
        AutomationProperties.SetName(_rangeBox, "Table range");
        AutomationProperties.SetAutomationId(_rangeBox, "CreateTableRangeBox");
        AutomationProperties.SetHelpText(_rangeBox, "Enter the range of worksheet cells to convert into a table.");
        AutomationProperties.SetName(_headersBox, "My table has headers");
        AutomationProperties.SetAutomationId(_headersBox, "CreateTableHeadersBox");
        AutomationProperties.SetHelpText(_headersBox, "Select when the first row of the table range contains column headers.");
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new Label { Content = "_Where is the data for your table?", Target = _rangeBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(CreateReferenceEditor(_rangeBox, "Select table range", RequestRangeSelection));
        _headersBox.Margin = new Thickness(0, 0, 0, 16);
        root.Children.Add(_headersBox);
        root.Children.Add(TextToColumnsDialog.CreateButtonRow(Accept));
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static bool TryParse(
        SheetId sheetId,
        string rangeText,
        bool firstRowHasHeaders,
        string tableStyleName,
        out CreateTableDialogResult result,
        out string? error) =>
        CreateTableInputParser.TryParse(sheetId, rangeText, firstRowHasHeaders, tableStyleName, out result, out error);

    public static CreateTableRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

    private static DockPanel CreateReferenceEditor(
        TextBox textBox,
        string automationName,
        Action<DialogReferencePickerRequest>? requestSelection)
    {
        var panel = DialogReferencePicker.CreateEditor(textBox, automationName, requestSelection: requestSelection);
        panel.Margin = new Thickness(0, 0, 0, 12);
        return panel;
    }

    private void RequestRangeSelection(DialogReferencePickerRequest request)
    {
        RangeSelectionRequest = CreateRangeSelectionRequest(request.CurrentText);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        FocusRangeBox();
    }

    public void ApplyRangeSelection(string rangeText)
    {
        _rangeBox.Text = rangeText;
        FocusRangeBox();
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusRangeBox();
    }

    private void FocusRangeBox()
    {
        DialogFocus.FocusAndSelect(_rangeBox);
    }

    private void Accept()
    {
        if (!TryParse(_sheetId, _rangeBox.Text, _headersBox.IsChecked == true, _tableStyleName, out var result, out var error))
        {
            DialogMessageHelper.ShowWarning(this, error ?? "Enter a valid table range.", Title);
            FocusRangeBox();
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
