using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record CreateTableRangeSelectionRequest(string CurrentText, bool CollapseDialog = true);

public sealed class CreateTableDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _rangeBox = new();
    private readonly CheckBox _headersBox = new()
    {
        Content = UiText.Get(CreateTableDialogPlanner.HeadersCheckBoxKey),
        IsChecked = CreateTableDialogPlanner.DefaultFirstRowHasHeaders
    };
    private readonly string _tableStyleName;
    private readonly Action<CreateTableRangeSelectionRequest>? _requestRangeSelection;

    public CreateTableDialogPlan? Result { get; private set; }
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
        Title = UiText.Get(CreateTableDialogPlanner.TitleKey);
        Width = CreateTableDialogPlanner.Width;
        Height = CreateTableDialogPlanner.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, CreateTableDialogPlanner.DialogAutomationId);

        _rangeBox.Text = defaultRangeText;
        AutomationProperties.SetName(_rangeBox, UiText.Get(CreateTableDialogPlanner.RangeAutomationNameKey));
        AutomationProperties.SetAutomationId(_rangeBox, CreateTableDialogPlanner.RangeBoxAutomationId);
        AutomationProperties.SetHelpText(_rangeBox, UiText.Get(CreateTableDialogPlanner.RangeAutomationHelpTextKey));
        AutomationProperties.SetName(_headersBox, UiText.Get(CreateTableDialogPlanner.HeadersAutomationNameKey));
        AutomationProperties.SetAutomationId(_headersBox, CreateTableDialogPlanner.HeadersBoxAutomationId);
        AutomationProperties.SetHelpText(_headersBox, UiText.Get(CreateTableDialogPlanner.HeadersAutomationHelpTextKey));
        var root = new StackPanel { Margin = new Thickness(CreateTableDialogPlanner.ContentMargin) };
        root.Children.Add(new Label
        {
            Content = UiText.Get(CreateTableDialogPlanner.RangeLabelKey),
            Target = _rangeBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, CreateTableDialogPlanner.RangeLabelBottomMargin)
        });
        root.Children.Add(CreateReferenceEditor(_rangeBox, UiText.Get(CreateTableDialogPlanner.RangePickerAutomationNameKey), RequestRangeSelection));
        _headersBox.Margin = new Thickness(0, 0, 0, CreateTableDialogPlanner.HeadersBottomMargin);
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
        out CreateTableDialogPlan result,
        out string? error)
    {
        if (CreateTableDialogPlanner.TryParse(
                sheetId,
                rangeText,
                firstRowHasHeaders,
                tableStyleName,
                out var plan,
                out var errorKey))
        {
            result = plan;
            error = null;
            return true;
        }

        result = default!;
        error = UiText.Get(errorKey ?? CreateTableDialogPlanner.InvalidRangeMessageKey);
        return false;
    }

    public static CreateTableRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

    private static DockPanel CreateReferenceEditor(
        TextBox textBox,
        string automationName,
        Action<DialogReferencePickerRequest>? requestSelection)
    {
        var panel = DialogReferencePicker.CreateEditor(textBox, automationName, requestSelection: requestSelection);
        panel.Margin = new Thickness(0, 0, 0, CreateTableDialogPlanner.RangeEditorBottomMargin);
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
            DialogFocus.ShowWarningAndFocus(this, error ?? UiText.Get("CreateTable_InvalidRangeMessage"), Title, _rangeBox);
            return;
        }

        Result = result;
        DialogResult = true;
    }
}
