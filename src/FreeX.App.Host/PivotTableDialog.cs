using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record PivotTableDialogResult(
    string SourceRangeText,
    PivotDestinationKind DestinationKind,
    string DestinationRangeText,
    bool OpenFieldList);

public enum PivotTableRangeSelectionTarget
{
    SourceRange,
    DestinationRange
}

public sealed record PivotTableRangeSelectionRequest(
    PivotTableRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);

public sealed class PivotTableDialog : Window
{
    private readonly Workbook _workbook;
    private readonly SheetId _sourceSheetId;
    private readonly TextBox _sourceRangeBox = new();
    private readonly TextBox _destinationRangeBox = new();
    private readonly RadioButton _selectTableRangeButton = new() { Content = UiText.Get("PivotTable_SelectATableOrRange"), IsChecked = true };
    private readonly RadioButton _newWorksheetButton = new() { Content = UiText.Get("PivotTable_NewWorksheet"), IsChecked = true };
    private readonly RadioButton _existingWorksheetButton = new() { Content = UiText.Get("PivotTable_ExistingWorksheet") };
    private readonly CheckBox _openFieldListBox = new() { Content = UiText.Get("PivotTable_OpenPivotTableFieldsPane"), IsChecked = true };
    private readonly Action<PivotTableRangeSelectionRequest>? _requestRangeSelection;

    public PivotTableDialogResult Result { get; private set; }
    public PivotTableRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public PivotTableDialog(
        Workbook workbook,
        SheetId sourceSheetId,
        GridRange sourceRange,
        Action<PivotTableRangeSelectionRequest>? requestRangeSelection = null)
    {
        _workbook = workbook;
        _sourceSheetId = sourceSheetId;
        _requestRangeSelection = requestRangeSelection;
        var sourceRangeText = PivotCreatePlanner.FormatRange(workbook, sourceSheetId, sourceRange);
        var destinationText = PivotCreatePlanner.FormatDefaultDestination(workbook, sourceSheetId, sourceRange);
        Result = CreateResult(
            sourceRangeText,
            PivotDestinationKind.NewWorksheet,
            destinationText,
            openFieldList: true);

        Title = UiText.Get("PivotTable_CreatePivotTable");
        DialogSizing.ApplyContentHeight(this, width: 500, minHeight: 320);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(16) };

        stack.Children.Add(CreateSectionHeader(UiText.Get("PivotTable_ChooseDataHeader")));
        _selectTableRangeButton.Margin = new Thickness(0, 0, 0, 6);
        stack.Children.Add(_selectTableRangeButton);
        _sourceRangeBox.Text = Result.SourceRangeText;
        AutomationProperties.SetName(_sourceRangeBox, UiText.Get("PivotTable_PivotTableSourceRange"));
        AddLabeledReferenceEditor(
            stack,
            UiText.Get("PivotTable_TableRangeLabel"),
            _sourceRangeBox,
            UiText.Get("PivotTable_SelectPivotTableSourceRange"),
            PivotTableRangeSelectionTarget.SourceRange,
            labelMargin: new Thickness(22, 0, 0, 4),
            editorMargin: new Thickness(22, 0, 0, 8));

        stack.Children.Add(CreateSectionHeader(UiText.Get("PivotTable_ChooseDestinationHeader")));
        _newWorksheetButton.Margin = new Thickness(0, 0, 0, 4);
        _newWorksheetButton.Checked += (_, _) => UpdateDestinationState();
        _existingWorksheetButton.Checked += (_, _) => UpdateDestinationState();
        stack.Children.Add(_newWorksheetButton);
        stack.Children.Add(_existingWorksheetButton);

        _destinationRangeBox.Text = destinationText;
        AutomationProperties.SetName(_destinationRangeBox, UiText.Get("PivotTable_PivotTableLocation"));
        AddLabeledReferenceEditor(
            stack,
            UiText.Get("PivotTable_LocationLabel"),
            _destinationRangeBox,
            UiText.Get("PivotTable_SelectPivotTableLocation"),
            PivotTableRangeSelectionTarget.DestinationRange,
            labelMargin: new Thickness(22, 4, 0, 4),
            editorMargin: new Thickness(22, 0, 0, 12));

        _openFieldListBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_openFieldListBox);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var ok = new Button { Content = UiText.Get("PivotTable_Create"), Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = UiText.Cancel, Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (!ValidateInputs())
                return;

            Result = CreateResult(
                _sourceRangeBox.Text,
                _existingWorksheetButton.IsChecked == true
                    ? PivotDestinationKind.ExistingWorksheet
                    : PivotDestinationKind.NewWorksheet,
                _destinationRangeBox.Text,
                _openFieldListBox.IsChecked == true);
            DialogResult = true;
        };
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        stack.Children.Add(btnRow);

        Content = stack;
        UpdateDestinationState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private static TextBlock CreateSectionHeader(string text) =>
        new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };

    public static PivotTableDialogResult CreateResult(
        string sourceRangeText,
        PivotDestinationKind destinationKind,
        string destinationRangeText,
        bool openFieldList) =>
        new(
            RequireRangeText(sourceRangeText, nameof(sourceRangeText)),
            destinationKind,
            destinationKind == PivotDestinationKind.NewWorksheet
                ? string.Empty
                : RequireRangeText(destinationRangeText, nameof(destinationRangeText)),
            openFieldList);

    private static string RequireRangeText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(UiText.Get("PivotTable_RangeTextIsRequired"), parameterName);

        return value.Trim();
    }

    private void AddLabeledReferenceEditor(
        Panel stack,
        string label,
        TextBox textBox,
        string automationName,
        PivotTableRangeSelectionTarget target,
        Thickness labelMargin,
        Thickness editorMargin)
    {
        stack.Children.Add(new Label
        {
            Content = label,
            Target = textBox,
            Padding = new Thickness(0),
            Margin = labelMargin
        });
        stack.Children.Add(CreateReferenceEditor(textBox, automationName, target, editorMargin));
    }

    public static PivotTableRangeSelectionRequest CreateRangeSelectionRequest(
        PivotTableRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

    private DockPanel CreateReferenceEditor(
        TextBox textBox,
        string automationName,
        PivotTableRangeSelectionTarget target,
        Thickness margin)
    {
        var panel = DialogReferencePicker.CreateEditor(
            textBox,
            automationName,
            requestSelection: request => RequestRangeSelection(target, request));
        panel.Margin = margin;
        return panel;
    }

    private void RequestRangeSelection(PivotTableRangeSelectionTarget target, DialogReferencePickerRequest request)
    {
        RangeSelectionRequest = CreateRangeSelectionRequest(target, request.CurrentText);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        FocusRangeSelectionInput(request.Target);
    }

    public void ApplyRangeSelection(PivotTableRangeSelectionTarget target, string rangeText)
    {
        var textBox = target == PivotTableRangeSelectionTarget.DestinationRange
            ? _destinationRangeBox
            : _sourceRangeBox;
        textBox.Text = rangeText;

        if (target == PivotTableRangeSelectionTarget.DestinationRange)
        {
            _existingWorksheetButton.IsChecked = true;
            UpdateDestinationState();
        }

        FocusRangeSelectionInput(textBox);
    }

    private bool ValidateInputs()
    {
        if (!WorkbookRangeTextCodec.TryParse(_sourceSheetId, _sourceRangeBox.Text, ResolveSheetIdByName, out _))
        {
            ShowInvalidInputWarning(UiText.Get("PivotTable_EnterValidSourceRange"), _sourceRangeBox);
            return false;
        }

        if (_existingWorksheetButton.IsChecked == true
            && (!WorkbookRangeTextCodec.TryParse(_sourceSheetId, _destinationRangeBox.Text, ResolveSheetIdByName, out var destinationRange)
                || destinationRange.Start.Sheet != _sourceSheetId))
        {
            ShowInvalidInputWarning(UiText.Get("PivotTable_EnterDestinationCellOnActiveWorksheet"), _destinationRangeBox);
            return false;
        }

        return true;
    }

    private SheetId? ResolveSheetIdByName(string sheetName)
    {
        foreach (var sheet in _workbook.Sheets)
        {
            if (string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                return sheet.Id;
        }

        return null;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
        return false;
    }

    private static void FocusRangeSelectionInput(TextBox target)
    {
        DialogFocus.FocusAndSelect(target);
    }

    private void UpdateDestinationState()
    {
        _destinationRangeBox.IsEnabled = _existingWorksheetButton.IsChecked == true;
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusRangeSelectionInput(_sourceRangeBox);
    }
}
