using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public enum SparklineKindChoice
{
    Line,
    Column,
    WinLoss
}

public enum SparklineRangeSelectionTarget
{
    DataRange,
    Location
}

public sealed record SparklineDialogResult(string DataRangeText, string LocationText, SparklineKindChoice Kind);

public sealed record SparklineRangeSelectionRequest(
    SparklineRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog);

public sealed class SparklineDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _dataRangeBox = new();
    private readonly TextBox _locationBox = new();
    private readonly ComboBox _kindBox = new();
    private readonly Button _dataRangePickerButton = new() { Content = UiText.Get("Sparkline_SelectDataRange"), Width = 132, ToolTip = UiText.Get("Sparkline_SelectDataRange2") };
    private readonly Button _locationPickerButton = new() { Content = UiText.Get("Sparkline_SelectLocationRange"), Width = 152, ToolTip = UiText.Get("Sparkline_SelectLocationRange2") };
    private readonly Action<SparklineRangeSelectionRequest>? _requestRangeSelection;

    public SparklineDialogResult Result { get; private set; }
    public SparklineRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public SparklineDialog(
        string dataRangeText,
        string locationText,
        SparklineKindChoice kind,
        Action<SparklineRangeSelectionRequest>? requestRangeSelection = null,
        SheetId sheetId = default)
    {
        _sheetId = sheetId;
        _requestRangeSelection = requestRangeSelection;
        Result = CreateResult(dataRangeText, locationText, kind);
        Title = UiText.Get("Sparkline_InsertSparkline");
        Width = 380;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _dataRangePickerButton.Click += (_, _) => RequestRangeSelection(SparklineRangeSelectionTarget.DataRange, _dataRangeBox);
        _locationPickerButton.Click += (_, _) => RequestRangeSelection(SparklineRangeSelectionTarget.Location, _locationBox);

        AutomationProperties.SetName(_dataRangePickerButton, UiText.Get("Sparkline_SelectSparklineDataRange"));
        AutomationProperties.SetAutomationId(_dataRangePickerButton, "SparklineDataRangePickerButton");
        AutomationProperties.SetHelpText(_dataRangePickerButton, UiText.Get("Sparkline_SelectTheWorksheetDataRangeForTheSparkline"));
        AutomationProperties.SetName(_locationPickerButton, UiText.Get("Sparkline_SelectSparklineLocationRange"));
        AutomationProperties.SetAutomationId(_locationPickerButton, "SparklineLocationRangePickerButton");
        AutomationProperties.SetHelpText(_locationPickerButton, UiText.Get("Sparkline_SelectTheDestinationCellForTheSparkline"));

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = UiText.Get("Sparkline_DataRange"), Target = _dataRangeBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _dataRangeBox.Text = Result.DataRangeText;
        AutomationProperties.SetName(_dataRangeBox, UiText.Get("Sparkline_SparklineDataRange"));
        AutomationProperties.SetAutomationId(_dataRangeBox, "SparklineDataRangeBox");
        AutomationProperties.SetHelpText(_dataRangeBox, UiText.Get("Sparkline_EnterTheWorksheetDataRangeForTheSparkline"));
        stack.Children.Add(CreateRangePickerRow(_dataRangeBox, _dataRangePickerButton));
        stack.Children.Add(new Label { Content = UiText.Get("Sparkline_LocationRange"), Target = _locationBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _locationBox.Text = Result.LocationText;
        AutomationProperties.SetName(_locationBox, UiText.Get("Sparkline_SparklineLocationRange"));
        AutomationProperties.SetAutomationId(_locationBox, "SparklineLocationRangeBox");
        AutomationProperties.SetHelpText(_locationBox, UiText.Get("Sparkline_EnterTheDestinationCellForTheSparkline"));
        stack.Children.Add(CreateRangePickerRow(_locationBox, _locationPickerButton));
        stack.Children.Add(new Label { Content = UiText.Get("Sparkline_SparklineType"), Target = _kindBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        AutomationProperties.SetName(_kindBox, UiText.Get("Sparkline_SparklineTypeAutomationName"));
        AutomationProperties.SetAutomationId(_kindBox, "SparklineTypeBox");
        AutomationProperties.SetHelpText(_kindBox, UiText.Get("Sparkline_ChooseWhetherTheSparklineIsLineColumnOrWinLoss"));
        _kindBox.ItemsSource = Enum.GetValues<SparklineKindChoice>()
            .Select(choice => new ComboBoxItem
            {
                Content = GetKindLabel(choice),
                Tag = choice
            });
        _kindBox.SelectedIndex = Math.Max(0, Array.IndexOf(Enum.GetValues<SparklineKindChoice>(), kind));
        _kindBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_kindBox);
        stack.Children.Add(DialogButtonRowFactory.Create(Accept, 72));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static SparklineDialogResult CreateResult(string dataRangeText, string locationText, SparklineKindChoice kind) =>
        SparklineDialogPlanner.CreateResult(dataRangeText, locationText, kind);

    public static SparklineRangeSelectionRequest CreateRangeSelectionRequest(
        SparklineRangeSelectionTarget target,
        string currentText) =>
        SparklineDialogPlanner.CreateRangeSelectionRequest(target, currentText);

    private void Accept()
    {
        if (!ValidateInputs())
            return;

        Result = CreateResult(
            _dataRangeBox.Text,
            _locationBox.Text,
            _kindBox.SelectedItem is ComboBoxItem { Tag: SparklineKindChoice kind } ? kind : SparklineKindChoice.Line);
        DialogResult = true;
    }

    private bool ValidateInputs()
    {
        return SparklineDialogPlanner.ValidateInputs(_dataRangeBox.Text, _locationBox.Text, _sheetId) switch
        {
            SparklineDialogValidationResult.InvalidDataRange =>
                ShowInvalidInputWarning(UiText.Get("Sparkline_InvalidDataRange"), _dataRangeBox),
            SparklineDialogValidationResult.InvalidLocation =>
                ShowInvalidInputWarning(UiText.Get("Sparkline_InvalidLocationCell"), _locationBox),
            _ => true
        };
    }

    private bool ShowInvalidInputWarning(string message, TextBox textBox)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
        FocusRangeSelectionInput(textBox);
        return false;
    }

    public static string GetKindLabel(SparklineKindChoice kind) =>
        SparklineDialogPlanner.GetKindLabel(kind);

    private void FocusInitialKeyboardTarget()
    {
        FocusRangeSelectionInput(_dataRangeBox);
    }

    private static StackPanel CreateRangePickerRow(TextBox textBox, Button pickerButton)
    {
        textBox.Height = 24;
        textBox.Width = 190;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(textBox);
        pickerButton.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(pickerButton);
        return row;
    }

    private void RequestRangeSelection(SparklineRangeSelectionTarget target, TextBox textBox)
    {
        FocusRangeSelectionInput(textBox);
        RangeSelectionRequest = CreateRangeSelectionRequest(target, textBox.Text);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        FocusRangeSelectionInput(textBox);
    }

    public void ApplyRangeSelection(SparklineRangeSelectionTarget target, string rangeText)
    {
        var textBox = target == SparklineRangeSelectionTarget.Location
            ? _locationBox
            : _dataRangeBox;
        textBox.Text = rangeText;
        FocusRangeSelectionInput(textBox);
    }

    private static void FocusRangeSelectionInput(TextBox textBox)
    {
        DialogFocus.FocusAndSelect(textBox);
    }
}
