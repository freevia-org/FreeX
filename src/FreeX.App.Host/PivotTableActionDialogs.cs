using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record PivotTableNameDialogResult(string Name);

public sealed class PivotTableNameDialog : Window
{
    private readonly TextBox _nameBox = new();

    public PivotTableNameDialogResult Result { get; private set; }

    public PivotTableNameDialog(string currentName)
    {
        Result = CreateResult(currentName);
        Title = UiText.Get("PivotTableName_Title");
        Width = 360;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _nameBox.Text = Result.Name;
        AutomationProperties.SetName(_nameBox, UiText.Get("PivotTableName_NameAutomationName"));
        Content = CreateContent();
        Loaded += (_, _) => DialogFocus.FocusAndSelect(_nameBox);
    }

    public static PivotTableNameDialogResult CreateResult(string? name) =>
        new(PivotUiPlanner.NormalizePivotTableName(name));

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        PivotDialogLayout.AddLabeledControl(
            stack,
            UiText.Get("PivotTableName_NameLabel"),
            _nameBox,
            _nameBox,
            new Thickness(0, 0, 0, 16));
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        return stack;
    }

    private void Accept()
    {
        var result = CreateResult(_nameBox.Text);
        if (string.IsNullOrWhiteSpace(result.Name))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("PivotTableName_EnterName"), Title);
            DialogFocus.FocusAndSelect(_nameBox);
            return;
        }

        Result = result;
        DialogResult = true;
    }
}

public sealed record MovePivotTableDialogResult(string DestinationRangeText);
public sealed record MovePivotTableRangeSelectionRequest(
    string CurrentText,
    bool CollapseDialog = true);

public sealed class MovePivotTableDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _destinationBox = new();
    private readonly Func<string, SheetId?> _resolveSheetId;
    private readonly Action<MovePivotTableRangeSelectionRequest>? _requestRangeSelection;

    public MovePivotTableDialogResult Result { get; private set; }
    public MovePivotTableRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public MovePivotTableDialog(
        string destinationRangeText,
        Action<MovePivotTableRangeSelectionRequest>? requestRangeSelection = null,
        SheetId sheetId = default,
        Func<string, SheetId?>? resolveSheetId = null)
    {
        _sheetId = sheetId;
        _resolveSheetId = resolveSheetId ?? (_ => null);
        _requestRangeSelection = requestRangeSelection;
        Result = CreateResult(destinationRangeText);
        Title = UiText.Get("MovePivotTable_Title");
        Width = 420;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _destinationBox.Text = Result.DestinationRangeText;
        AutomationProperties.SetName(_destinationBox, UiText.Get("MovePivotTable_DestinationAutomationName"));
        Content = CreateContent();
        Loaded += (_, _) => FocusRangeSelectionInput(_destinationBox);
    }

    public static MovePivotTableDialogResult CreateResult(string destinationRangeText) =>
        new(destinationRangeText.Trim());

    public static MovePivotTableRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

    public void ApplyRangeSelection(string rangeText)
    {
        _destinationBox.Text = rangeText;
        FocusRangeSelectionInput(_destinationBox);
    }

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        PivotDialogLayout.AddLabeledControl(
            stack,
            UiText.Get("MovePivotTable_DestinationLabel"),
            CreateReferenceEditor(_destinationBox, UiText.Get("MovePivotTable_SelectDestination")),
            _destinationBox,
            new Thickness(0, 0, 0, 16));
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        return stack;
    }

    private DockPanel CreateReferenceEditor(TextBox textBox, string automationName) =>
        DialogReferencePicker.CreateEditor(
            textBox,
            automationName,
            requestSelection: request =>
            {
                RangeSelectionRequest = CreateRangeSelectionRequest(request.CurrentText);
                _requestRangeSelection?.Invoke(RangeSelectionRequest);
                FocusRangeSelectionInput(request.Target);
            });

    private void Accept()
    {
        if (!WorkbookRangeTextCodec.TryParse(_sheetId, _destinationBox.Text, ResolveSheetIdByName, out _))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("MovePivotTable_EnterValidDestination"), Title);
            FocusRangeSelectionInput(_destinationBox);
            return;
        }

        Result = CreateResult(_destinationBox.Text);
        DialogResult = true;
    }

    private SheetId? ResolveSheetIdByName(string sheetName) => _resolveSheetId(sheetName);

    private static void FocusRangeSelectionInput(TextBox target)
    {
        DialogFocus.FocusAndSelect(target);
    }
}
