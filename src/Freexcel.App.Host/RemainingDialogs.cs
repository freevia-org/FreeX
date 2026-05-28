using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public sealed record ConditionalFormatThresholdDialogResult(string ThresholdText);

public sealed class ConditionalFormatThresholdDialog : Window
{
    private readonly TextBox _thresholdBox = new();

    public ConditionalFormatThresholdDialogResult Result { get; private set; }

    public ConditionalFormatThresholdDialog(string thresholdText = "0")
    {
        Result = CreateResult(thresholdText);
        Title = "New Formatting Rule";
        Width = 360;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _thresholdBox.Text = Result.ThresholdText;
        AutomationProperties.SetName(_thresholdBox, "Conditional format threshold");
        Content = ObjectSizeDialog.CreateSingleInputContent("Format cells greater _than:", _thresholdBox, Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.FocusAndSelect(_thresholdBox);
    }

    public static ConditionalFormatThresholdDialogResult CreateResult(string thresholdText) =>
        new(thresholdText.Trim());

    public static bool TryCreateResult(string? thresholdText, out ConditionalFormatThresholdDialogResult result, out string? error)
    {
        result = CreateResult(thresholdText ?? "");
        if (string.IsNullOrWhiteSpace(result.ThresholdText))
        {
            error = "Enter a threshold value.";
            return false;
        }

        error = null;
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_thresholdBox.Text, out var result, out var error))
        {
            ShowInvalidInputWarning(error ?? "Enter a threshold value.");
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void ShowInvalidInputWarning(string message)
    {
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        DialogFocus.FocusAndSelect(_thresholdBox);
    }
}

public sealed record RowHeightDialogResult(double Height);

public sealed class RowHeightDialog : Window
{
    private const double MaximumExcelRowHeight = 409.5;
    private readonly TextBox _heightBox = new();

    public RowHeightDialogResult Result { get; private set; } = new(20);

    public RowHeightDialog(double height = 20)
    {
        Result = new RowHeightDialogResult(height);
        Title = "Row Height";
        Width = 320;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _heightBox.Text = height.ToString(CultureInfo.InvariantCulture);
        AutomationProperties.SetName(_heightBox, "Row height");
        Content = ObjectSizeDialog.CreateSingleInputContent("Row _height:", _heightBox, Accept);
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
        result = new RowHeightDialogResult(20);
        error = null;
        if (input is null || !WorksheetSizeInputParser.TryParseSizeInRange(input, 0, MaximumExcelRowHeight, out var height))
        {
            error = "Enter a row height from 0 to 409.5.";
            return false;
        }

        result = new RowHeightDialogResult(height);
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_heightBox.Text, out var result, out var error))
        {
            MessageBox.Show(
                this,
                error ?? "Enter a row height from 0 to 409.5.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            FocusInvalidHeightInput();
            return;
        }

        Result = result;
        DialogResult = true;
    }
}

public sealed record ColumnWidthDialogResult(double Width);

public sealed class ColumnWidthDialog : Window
{
    private const double MaximumExcelColumnWidth = 255;
    private readonly TextBox _widthBox = new();

    public ColumnWidthDialogResult Result { get; private set; } = new(8);

    public ColumnWidthDialog(double width = 8)
    {
        Result = new ColumnWidthDialogResult(width);
        Title = "Column Width";
        Width = 320;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _widthBox.Text = width.ToString(CultureInfo.InvariantCulture);
        AutomationProperties.SetName(_widthBox, "Column width");
        Content = ObjectSizeDialog.CreateSingleInputContent("Column _width:", _widthBox, Accept);
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
        result = new ColumnWidthDialogResult(8);
        error = null;
        if (input is null || !WorksheetSizeInputParser.TryParseSizeInRange(input, 0, MaximumExcelColumnWidth, out var width))
        {
            error = "Enter a column width from 0 to 255.";
            return false;
        }

        result = new ColumnWidthDialogResult(width);
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_widthBox.Text, out var result, out var error))
        {
            MessageBox.Show(
                this,
                error ?? "Enter a column width from 0 to 255.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            FocusInvalidWidthInput();
            return;
        }

        Result = result;
        DialogResult = true;
    }
}

