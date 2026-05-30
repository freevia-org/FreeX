using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class SelectDataSourceDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _rangeBox = new();
    private readonly CheckBox _firstColumnCategoriesBox = new() { Content = UiText.Get("SelectDataSource_FirstColumnCategories") };
    private readonly CheckBox _switchRowColumnBox = new() { Content = UiText.Get("SelectDataSource_SwitchRowColumn") };
    private readonly ListBox _seriesList = new() { Height = 72 };
    private readonly ListBox _axisLabelsList = new() { Height = 72 };
    private readonly Action<SelectDataSourceRangeSelectionRequest>? _requestRangeSelection;
    private Button? _editSeriesButton;
    private Button? _removeSeriesButton;
    private Button? _editAxisLabelsButton;

    public SelectDataSourceDialogResult Result { get; private set; }
    public SelectDataSourceRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public SelectDataSourceDialog(
        string sourceRangeText,
        bool firstColumnIsCategories = true,
        Action<SelectDataSourceRangeSelectionRequest>? requestRangeSelection = null,
        SheetId sheetId = default)
    {
        _sheetId = sheetId;
        _requestRangeSelection = requestRangeSelection;
        Result = CreateResult(sourceRangeText, firstColumnIsCategories);
        Title = UiText.Get("SelectDataSource_Title");
        Width = 620;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = UiText.Get("SelectDataSource_ChartDataRangeLabel"), Target = _rangeBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _rangeBox.Text = Result.SourceRangeText;
        AutomationProperties.SetName(_rangeBox, UiText.Get("SelectDataSource_ChartDataRangeAutomationName"));
        stack.Children.Add(CreateReferenceEditor(_rangeBox, UiText.Get("SelectDataSource_SelectChartDataRangeAutomationName")));
        _switchRowColumnBox.Margin = new Thickness(0, 10, 0, 8);
        stack.Children.Add(_switchRowColumnBox);
        _seriesList.MouseDoubleClick += EditSeriesButton_Click;
        _seriesList.SelectionChanged += (_, _) => UpdateActionButtonState();
        _axisLabelsList.MouseDoubleClick += EditAxisLabelsButton_Click;
        _axisLabelsList.SelectionChanged += (_, _) => UpdateActionButtonState();
        stack.Children.Add(CreateSourceListPanel(
            UiText.Get("SelectDataSource_SeriesPanelTitle"),
            UiText.Get("SelectDataSource_SeriesListAutomationName"),
            UiText.Get("SelectDataSource_SeriesListHelpText"),
            _seriesList,
            ((UiText.Get("SelectDataSource_AddSeriesButton"), AddSeriesButton_Click), (UiText.Get("SelectDataSource_EditSeriesButton"), EditSeriesButton_Click), (UiText.Get("SelectDataSource_RemoveSeriesButton"), RemoveSeriesButton_Click))));
        stack.Children.Add(CreateSourceListPanel(
            UiText.Get("SelectDataSource_AxisLabelsPanelTitle"),
            UiText.Get("SelectDataSource_AxisLabelsListAutomationName"),
            UiText.Get("SelectDataSource_AxisLabelsListHelpText"),
            _axisLabelsList,
            ((UiText.Get("SelectDataSource_EditAxisLabelsButton"), EditAxisLabelsButton_Click), null, null)));
        _firstColumnCategoriesBox.IsChecked = firstColumnIsCategories;
        _firstColumnCategoriesBox.Margin = new Thickness(0, 10, 0, 8);
        stack.Children.Add(_firstColumnCategoriesBox);
        _firstColumnCategoriesBox.Checked += (_, _) => RefreshPreviewLists();
        _firstColumnCategoriesBox.Unchecked += (_, _) => RefreshPreviewLists();
        _rangeBox.TextChanged += (_, _) => RefreshPreviewLists();
        var hiddenEmptyButton = new Button
        {
            Content = UiText.Get("SelectDataSource_HiddenEmptyCellsButton"),
            Width = 150,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 16)
        };
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
        if (!ChartInputParser.TryParseDataRange(_rangeBox.Text, _sheetId, out _))
        {
            ShowInvalidInputWarning(UiText.Get("SelectDataSource_InvalidRangeMessage"), _rangeBox);
            return false;
        }

        return true;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
        FocusRangeSelectionInput(target);
        return false;
    }

}
