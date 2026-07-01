using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Host;

public sealed class ConditionalFormatThresholdDialog : Window
{
    private readonly TextBox _thresholdBox = new();

    public ConditionalFormatThresholdDialogResult Result { get; private set; }

    public ConditionalFormatThresholdDialog(string thresholdText = "0")
    {
        Result = CreateResult(thresholdText);
        Title = UiText.Get("Remaining_NewFormattingRule");
        Width = 360;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _thresholdBox.Text = Result.ThresholdText;
        AutomationProperties.SetName(_thresholdBox, UiText.Get("Remaining_ConditionalFormatThreshold"));
        AutomationProperties.SetAutomationId(_thresholdBox, "ConditionalFormatThresholdBox");
        AutomationProperties.SetHelpText(_thresholdBox, UiText.Get("Remaining_EnterTheValueForTheConditionalFormattingRuleThreshold"));
        Content = ObjectSizeDialog.CreateSingleInputContent(UiText.Get("Remaining_FormatCellsGreaterThan"), _thresholdBox, Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.FocusAndSelect(_thresholdBox);
    }

    public static ConditionalFormatThresholdDialogResult CreateResult(string thresholdText) =>
        ConditionalFormatThresholdDialogPlanner.CreateResult(thresholdText);

    public static bool TryCreateResult(string? thresholdText, out ConditionalFormatThresholdDialogResult result, out string? error)
    {
        if (!ConditionalFormatThresholdDialogPlanner.TryCreateResult(thresholdText, out result))
        {
            error = UiText.Get("Remaining_EnterThresholdValue");
            return false;
        }

        error = null;
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_thresholdBox.Text, out var result, out var error))
        {
            ShowInvalidInputWarning(error ?? UiText.Get("Remaining_EnterThresholdValue"));
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void ShowInvalidInputWarning(string message)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, _thresholdBox);
    }
}

public sealed class RowHeightDialog : Window
{
    private readonly TextBox _heightBox = new();

    public RowHeightDialogResult Result { get; private set; } = new(WorksheetDimensionDialogPlanner.DefaultRowHeight);

    public RowHeightDialog(double height = 20)
    {
        Result = new RowHeightDialogResult(height);
        Title = UiText.Get("Remaining_RowHeight");
        Width = 320;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _heightBox.Text = height.ToString(CultureInfo.InvariantCulture);
        AutomationProperties.SetName(_heightBox, UiText.Get("Remaining_RowHeight"));
        AutomationProperties.SetAutomationId(_heightBox, "RowHeightBox");
        AutomationProperties.SetHelpText(_heightBox, UiText.Get("Remaining_EnterARowHeightFrom0To4095"));
        Content = ObjectSizeDialog.CreateSingleInputContent(UiText.Get("Remaining_RowHeight2"), _heightBox, Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusInvalidHeightInput();
    }

    private void FocusInvalidHeightInput()
    {
        DialogFocus.FocusAndSelect(_heightBox);
    }

    public static bool TryCreateResult(string? input, out RowHeightDialogResult result, out string? error)
    {
        result = new RowHeightDialogResult(WorksheetDimensionDialogPlanner.DefaultRowHeight);
        error = null;
        if (!WorksheetDimensionDialogPlanner.TryCreateRowHeightResult(input, out result))
        {
            error = UiText.Get("Remaining_EnterRowHeightFrom0To4095");
            return false;
        }

        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_heightBox.Text, out var result, out var error))
        {
            DialogFocus.ShowWarningAndFocus(this, error ?? UiText.Get("Remaining_EnterARowHeightFrom0To409"), Title, _heightBox);
            return;
        }

        Result = result;
        DialogResult = true;
    }
}

public sealed class ColumnWidthDialog : Window
{
    private readonly TextBox _widthBox = new();

    public ColumnWidthDialogResult Result { get; private set; } = new(WorksheetDimensionDialogPlanner.DefaultColumnWidth);

    public ColumnWidthDialog(double width = 8)
    {
        Result = new ColumnWidthDialogResult(width);
        Title = UiText.Get("Remaining_ColumnWidth");
        Width = 320;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _widthBox.Text = width.ToString(CultureInfo.InvariantCulture);
        AutomationProperties.SetName(_widthBox, UiText.Get("Remaining_ColumnWidth"));
        AutomationProperties.SetAutomationId(_widthBox, "ColumnWidthBox");
        AutomationProperties.SetHelpText(_widthBox, UiText.Get("Remaining_EnterAColumnWidthFrom0To255"));
        Content = ObjectSizeDialog.CreateSingleInputContent(UiText.Get("Remaining_ColumnWidth2"), _widthBox, Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusInvalidWidthInput();
    }

    private void FocusInvalidWidthInput()
    {
        DialogFocus.FocusAndSelect(_widthBox);
    }

    public static bool TryCreateResult(string? input, out ColumnWidthDialogResult result, out string? error)
    {
        result = new ColumnWidthDialogResult(WorksheetDimensionDialogPlanner.DefaultColumnWidth);
        error = null;
        if (!WorksheetDimensionDialogPlanner.TryCreateColumnWidthResult(input, out result))
        {
            error = UiText.Get("Remaining_EnterColumnWidthFrom0To255");
            return false;
        }

        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_widthBox.Text, out var result, out var error))
        {
            DialogFocus.ShowWarningAndFocus(this, error ?? UiText.Get("Remaining_EnterAColumnWidthFrom0To255"), Title, _widthBox);
            return;
        }

        Result = result;
        DialogResult = true;
    }
}

