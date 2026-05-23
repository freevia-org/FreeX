using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum PivotTableDestinationKind
{
    NewWorksheet,
    ExistingWorksheet
}

public sealed record PivotTableDialogResult(
    string SourceRangeText,
    PivotTableDestinationKind DestinationKind,
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
    private readonly TextBox _sourceRangeBox = new();
    private readonly TextBox _destinationRangeBox = new();
    private readonly RadioButton _selectTableRangeButton = new() { Content = "Select a _table or range", IsChecked = true };
    private readonly RadioButton _newWorksheetButton = new() { Content = "_New worksheet", IsChecked = true };
    private readonly RadioButton _existingWorksheetButton = new() { Content = "_Existing worksheet" };
    private readonly CheckBox _openFieldListBox = new() { Content = "Open PivotTable _Fields pane", IsChecked = true };
    private readonly Action<PivotTableRangeSelectionRequest>? _requestRangeSelection;

    public PivotTableDialogResult Result { get; private set; }
    public PivotTableRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public PivotTableDialog(
        Workbook workbook,
        SheetId sourceSheetId,
        GridRange sourceRange,
        Action<PivotTableRangeSelectionRequest>? requestRangeSelection = null)
    {
        _requestRangeSelection = requestRangeSelection;
        var sourceRangeText = FormatRange(workbook, sourceSheetId, sourceRange);
        var destinationText = FormatDestination(workbook, sourceSheetId, sourceRange);
        Result = CreateResult(
            sourceRangeText,
            PivotTableDestinationKind.NewWorksheet,
            destinationText,
            openFieldList: true);

        Title = "Create PivotTable";
        Width = 500;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(16) };

        stack.Children.Add(CreateSectionHeader("Choose the data that you want to analyze"));
        _selectTableRangeButton.Margin = new Thickness(0, 0, 0, 6);
        stack.Children.Add(_selectTableRangeButton);
        _sourceRangeBox.Text = Result.SourceRangeText;
        AddLabeledReferenceEditor(
            stack,
            "Table/_Range:",
            _sourceRangeBox,
            "Select PivotTable source range",
            PivotTableRangeSelectionTarget.SourceRange,
            labelMargin: new Thickness(22, 0, 0, 4),
            editorMargin: new Thickness(22, 0, 0, 8));

        stack.Children.Add(CreateSectionHeader("Choose where you want the PivotTable report to be placed"));
        _newWorksheetButton.Margin = new Thickness(0, 0, 0, 4);
        _newWorksheetButton.Checked += (_, _) => UpdateDestinationState();
        _existingWorksheetButton.Checked += (_, _) => UpdateDestinationState();
        stack.Children.Add(_newWorksheetButton);
        stack.Children.Add(_existingWorksheetButton);

        _destinationRangeBox.Text = destinationText;
        AddLabeledReferenceEditor(
            stack,
            "_Location:",
            _destinationRangeBox,
            "Select PivotTable location",
            PivotTableRangeSelectionTarget.DestinationRange,
            labelMargin: new Thickness(22, 4, 0, 4),
            editorMargin: new Thickness(22, 0, 0, 12));

        _openFieldListBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_openFieldListBox);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var ok = new Button { Content = "_Create", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "_Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            Result = CreateResult(
                _sourceRangeBox.Text,
                _existingWorksheetButton.IsChecked == true
                    ? PivotTableDestinationKind.ExistingWorksheet
                    : PivotTableDestinationKind.NewWorksheet,
                _destinationRangeBox.Text,
                _openFieldListBox.IsChecked == true);
            DialogResult = true;
        };
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        stack.Children.Add(btnRow);

        Content = stack;
        UpdateDestinationState();
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
        PivotTableDestinationKind destinationKind,
        string destinationRangeText,
        bool openFieldList) =>
        new(
            RequireRangeText(sourceRangeText, nameof(sourceRangeText)),
            destinationKind,
            destinationKind == PivotTableDestinationKind.NewWorksheet
                ? string.Empty
                : RequireRangeText(destinationRangeText, nameof(destinationRangeText)),
            openFieldList);

    private static string FormatRange(Workbook workbook, SheetId sheetId, GridRange range)
    {
        var sheetName = workbook.GetSheet(sheetId)?.Name;
        var address = $"{range.Start.ToA1()}:{range.End.ToA1()}";
        return string.IsNullOrWhiteSpace(sheetName)
            ? address
            : $"{PivotUiPlanner.QuoteSheetNameForReference(sheetName)}!{address}";
    }

    private static string FormatDestination(Workbook workbook, SheetId sheetId, GridRange sourceRange)
    {
        var sheetName = workbook.GetSheet(sheetId)?.Name;
        var col = Math.Min(sourceRange.End.Col + 2, CellAddress.MaxCol);
        var address = new CellAddress(sheetId, sourceRange.Start.Row, col).ToA1();
        return string.IsNullOrWhiteSpace(sheetName)
            ? address
            : $"{PivotUiPlanner.QuoteSheetNameForReference(sheetName)}!{address}";
    }

    private static string RequireRangeText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Range text is required.", parameterName);

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
    }

    private void UpdateDestinationState()
    {
        _destinationRangeBox.IsEnabled = _existingWorksheetButton.IsChecked == true;
    }
}
