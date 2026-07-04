using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class SelectDataSourceDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _rangeBox = new();
    private readonly CheckBox _firstColumnCategoriesBox = new() { Content = FieldLabel(SelectDataSourcePlanner.GetFirstColumnCategoriesField()) };
    private readonly CheckBox _switchRowColumnBox = new() { Content = FieldLabel(SelectDataSourcePlanner.GetSwitchRowColumnField()) };
    private readonly ListBox _seriesList = new() { Height = 72 };
    private readonly ListBox _axisLabelsList = new() { Height = 72 };
    private readonly Action<SelectDataSourceRangeSelectionRequest>? _requestRangeSelection;
    private readonly Func<string, SheetId?> _resolveSheetId;
    private Button? _editSeriesButton;
    private Button? _removeSeriesButton;
    private Button? _editAxisLabelsButton;

    public SelectDataSourceDialogResult Result { get; private set; }
    public SelectDataSourceRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public SelectDataSourceDialog(
        string sourceRangeText,
        bool firstColumnIsCategories = true,
        Action<SelectDataSourceRangeSelectionRequest>? requestRangeSelection = null,
        SheetId sheetId = default,
        Func<string, SheetId?>? resolveSheetId = null,
        bool switchRowColumn = false)
    {
        _sheetId = sheetId;
        _requestRangeSelection = requestRangeSelection;
        _resolveSheetId = resolveSheetId ?? (_ => null);
        Result = CreateResult(sourceRangeText, firstColumnIsCategories, switchRowColumn);
        Title = UiText.Get(SelectDataSourcePlanner.DialogTitleResourceKey);
        Width = 620;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var rangeField = SelectDataSourcePlanner.GetChartDataRangeField();
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = FieldLabel(rangeField), Target = _rangeBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _rangeBox.Text = Result.SourceRangeText;
        AutomationProperties.SetName(_rangeBox, UiText.Get(rangeField.AutomationNameResourceKey!));
        AutomationProperties.SetAutomationId(_rangeBox, rangeField.AutomationId);
        stack.Children.Add(CreateReferenceEditor(_rangeBox, UiText.Get(SelectDataSourcePlanner.SelectRangeAutomationNameResourceKey)));
        _switchRowColumnBox.Margin = new Thickness(0, 10, 0, 8);
        _switchRowColumnBox.IsChecked = switchRowColumn;
        AutomationProperties.SetAutomationId(_switchRowColumnBox, SelectDataSourcePlanner.GetSwitchRowColumnField().AutomationId);
        stack.Children.Add(_switchRowColumnBox);
        _seriesList.MouseDoubleClick += EditSeriesButton_Click;
        _seriesList.SelectionChanged += (_, _) => UpdateActionButtonState();
        _axisLabelsList.MouseDoubleClick += EditAxisLabelsButton_Click;
        _axisLabelsList.SelectionChanged += (_, _) => UpdateActionButtonState();
        stack.Children.Add(CreateSourceListPanel(
            SelectDataSourcePlanner.GetSeriesPanel(),
            _seriesList,
            new Dictionary<SelectDataSourceDialogActionId, RoutedEventHandler>
            {
                [SelectDataSourceDialogActionId.AddSeries] = AddSeriesButton_Click,
                [SelectDataSourceDialogActionId.EditSeries] = EditSeriesButton_Click,
                [SelectDataSourceDialogActionId.RemoveSeries] = RemoveSeriesButton_Click,
            }));
        stack.Children.Add(CreateSourceListPanel(
            SelectDataSourcePlanner.GetAxisLabelsPanel(),
            _axisLabelsList,
            new Dictionary<SelectDataSourceDialogActionId, RoutedEventHandler>
            {
                [SelectDataSourceDialogActionId.EditAxisLabels] = EditAxisLabelsButton_Click,
            }));
        _firstColumnCategoriesBox.IsChecked = firstColumnIsCategories;
        _firstColumnCategoriesBox.Margin = new Thickness(0, 10, 0, 8);
        AutomationProperties.SetAutomationId(_firstColumnCategoriesBox, SelectDataSourcePlanner.GetFirstColumnCategoriesField().AutomationId);
        stack.Children.Add(_firstColumnCategoriesBox);
        _firstColumnCategoriesBox.Checked += (_, _) => RefreshPreviewLists();
        _firstColumnCategoriesBox.Unchecked += (_, _) => RefreshPreviewLists();
        _rangeBox.TextChanged += (_, _) => RefreshPreviewLists();
        var hiddenEmptyAction = SelectDataSourcePlanner.GetHiddenEmptyCellsAction();
        var hiddenEmptyButton = new Button
        {
            Content = UiText.Get(hiddenEmptyAction.LabelResourceKey),
            Width = 150,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 16)
        };
        AutomationProperties.SetAutomationId(hiddenEmptyButton, hiddenEmptyAction.AutomationId);
        hiddenEmptyButton.Click += HiddenEmptyCellsButton_Click;
        stack.Children.Add(hiddenEmptyButton);
        RefreshPreviewLists();
        stack.Children.Add(InsertChartDialog.CreateButtonRow(() =>
        {
            if (!ValidateInputs())
                return;

            Result = CreateResult(
                _rangeBox.Text,
                _firstColumnCategoriesBox.IsChecked == true,
                _switchRowColumnBox.IsChecked == true);
            DialogResult = true;
        }));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusRangeSelectionInput(_rangeBox);
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

    public void ApplyRangeSelection(string rangeText)
    {
        _rangeBox.Text = rangeText;
        FocusRangeSelectionInput(_rangeBox);
    }

    private static void FocusRangeSelectionInput(TextBox target)
    {
        DialogFocus.FocusAndSelect(target);
    }

    private bool ValidateInputs()
    {
        if (!ChartInputParser.TryParseDataRange(_rangeBox.Text, _sheetId, _resolveSheetId, out _))
        {
            ShowInvalidInputWarning(UiText.Get(SelectDataSourcePlanner.InvalidRangeMessageResourceKey), _rangeBox);
            return false;
        }

        return true;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
        return false;
    }

    private static string FieldLabel(SelectDataSourceDialogFieldDescriptor field) =>
        UiText.Get(field.LabelResourceKey);
}
