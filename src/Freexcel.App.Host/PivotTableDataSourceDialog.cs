using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record PivotTableDataSourceDialogResult(string SourceRangeText);
public sealed record PivotTableDataSourceRangeSelectionRequest(
    string CurrentText,
    bool CollapseDialog = true);

public sealed class PivotTableDataSourceDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _sourceBox = new();
    private readonly Func<string, SheetId?> _resolveSheetId;
    private readonly Action<PivotTableDataSourceRangeSelectionRequest>? _requestRangeSelection;

    public PivotTableDataSourceDialogResult Result { get; private set; }
    public PivotTableDataSourceRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public PivotTableDataSourceDialog(
        string sourceRangeText,
        Action<PivotTableDataSourceRangeSelectionRequest>? requestRangeSelection = null,
        SheetId sheetId = default,
        Func<string, SheetId?>? resolveSheetId = null)
    {
        _sheetId = sheetId;
        _resolveSheetId = resolveSheetId ?? (_ => null);
        _requestRangeSelection = requestRangeSelection;
        Result = CreateResult(sourceRangeText);
        Title = "Change PivotTable Data Source";
        Width = 420;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _sourceBox.Text = Result.SourceRangeText;
        AutomationProperties.SetName(_sourceBox, "PivotTable source range");
        Content = CreateContent();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static PivotTableDataSourceDialogResult CreateResult(string sourceRangeText) =>
        new(sourceRangeText.Trim());

    public static PivotTableDataSourceRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

    public void ApplyRangeSelection(string rangeText)
    {
        _sourceBox.Text = rangeText;
        FocusRangeSelectionInput(_sourceBox);
    }

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        PivotDialogLayout.AddLabeledControl(
            stack,
            "Table/_Range:",
            CreateReferenceEditor(_sourceBox, "Select PivotTable source range"),
            _sourceBox,
            new Thickness(0, 0, 0, 16));
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(() =>
        {
            if (!ValidateInputs())
                return;

            Result = CreateResult(_sourceBox.Text);
            DialogResult = true;
        }));
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

    private bool ValidateInputs()
    {
        if (!WorkbookRangeTextCodec.TryParse(_sheetId, _sourceBox.Text, ResolveSheetIdByName, out _))
        {
            ShowInvalidInputWarning("Enter a valid PivotTable source range.", _sourceBox);
            return false;
        }

        return true;
    }

    private SheetId? ResolveSheetIdByName(string sheetName) => _resolveSheetId(sheetName);

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        FocusRangeSelectionInput(target);
        return false;
    }

    private static void FocusRangeSelectionInput(TextBox target)
    {
        DialogFocus.FocusAndSelect(target);
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusRangeSelectionInput(_sourceBox);
    }
}
