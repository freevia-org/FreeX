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
    private readonly CheckBox _headersBox = new() { Content = UiText.Get("CreateTable_HeadersCheckBox"), IsChecked = true };
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
        Title = UiText.Get("CreateTable_Title");
        Width = 360;
        Height = 190;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _rangeBox.Text = defaultRangeText;
        AutomationProperties.SetName(_rangeBox, UiText.Get("CreateTable_RangeAutomationName"));
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new Label { Content = UiText.Get("CreateTable_RangeLabel"), Target = _rangeBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(CreateReferenceEditor(_rangeBox, UiText.Get("CreateTable_RangePickerAutomationName"), RequestRangeSelection));
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
        out string? error)
    {
        var parsed = CreateTableInputParser.TryParse(sheetId, rangeText, firstRowHasHeaders, tableStyleName, out result, out error);
        error = LocalizeParseError(error);
        return parsed;
    }

    private static string? LocalizeParseError(string? error) =>
        error switch
        {
            "Enter a table range." => UiText.Get("CreateTable_MissingRangeMessage"),
            "Table range must include at least two rows." => UiText.Get("CreateTable_MinimumRowsMessage"),
            "Enter a valid table range." => UiText.Get("CreateTable_InvalidRangeMessage"),
            _ => error
        };

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
            DialogMessageHelper.ShowWarning(this, error ?? UiText.Get("CreateTable_InvalidRangeMessage"), Title);
            FocusRangeBox();
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
