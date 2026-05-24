using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        Content = ObjectSizeDialog.CreateSingleInputContent("Format cells greater _than:", _thresholdBox, Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        _thresholdBox.Focus();
        _thresholdBox.SelectAll();
        Keyboard.Focus(_thresholdBox);
    }

    public static ConditionalFormatThresholdDialogResult CreateResult(string thresholdText) =>
        new(thresholdText.Trim());

    private void Accept()
    {
        Result = CreateResult(_thresholdBox.Text);
        if (string.IsNullOrWhiteSpace(Result.ThresholdText))
            return;
        DialogResult = true;
    }
}

public sealed record RowHeightDialogResult(double Height);

public sealed class RowHeightDialog : Window
{
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
        Content = ObjectSizeDialog.CreateSingleInputContent("Row _height:", _heightBox, Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        _heightBox.Focus();
        _heightBox.SelectAll();
        Keyboard.Focus(_heightBox);
    }

    public static bool TryCreateResult(string? input, out RowHeightDialogResult result, out string? error)
    {
        result = new RowHeightDialogResult(20);
        error = null;
        if (input is null || !WorksheetSizeInputParser.TryParsePositiveSize(input, out var height))
        {
            error = "Enter a positive row height.";
            return false;
        }

        result = new RowHeightDialogResult(height);
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_heightBox.Text, out var result, out _))
            return;
        Result = result;
        DialogResult = true;
    }
}

public sealed record ColumnWidthDialogResult(double Width);

public sealed class ColumnWidthDialog : Window
{
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
        Content = ObjectSizeDialog.CreateSingleInputContent("Column _width:", _widthBox, Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        _widthBox.Focus();
        _widthBox.SelectAll();
        Keyboard.Focus(_widthBox);
    }

    public static bool TryCreateResult(string? input, out ColumnWidthDialogResult result, out string? error)
    {
        result = new ColumnWidthDialogResult(8);
        error = null;
        if (input is null || !WorksheetSizeInputParser.TryParsePositiveSize(input, out var width))
        {
            error = "Enter a positive column width.";
            return false;
        }

        result = new ColumnWidthDialogResult(width);
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_widthBox.Text, out var result, out _))
            return;
        Result = result;
        DialogResult = true;
    }
}

public sealed record SheetNameDialogResult(string SheetName);

public sealed class SheetNameDialog : Window
{
    private readonly TextBox _nameBox = new();

    public SheetNameDialogResult Result { get; private set; }

    public SheetNameDialog(string currentName)
    {
        Result = CreateResult(currentName);
        Title = "Rename Sheet";
        Width = 340;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _nameBox.Text = currentName;
        Content = ObjectSizeDialog.CreateSingleInputContent("Sheet _name:", _nameBox, () =>
        {
            Result = CreateResult(_nameBox.Text);
            DialogResult = true;
        });
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static SheetNameDialogResult CreateResult(string sheetName) => new(sheetName.Trim());

    private void FocusInitialKeyboardTarget()
    {
        _nameBox.Focus();
        _nameBox.SelectAll();
        Keyboard.Focus(_nameBox);
    }
}

public sealed record UnhideSheetDialogResult(string SheetName);

public sealed class UnhideSheetDialog : Window
{
    private readonly ListBox _sheetBox = new();

    public UnhideSheetDialogResult Result { get; private set; }

    public UnhideSheetDialog(IEnumerable<string> hiddenSheetNames)
    {
        var names = hiddenSheetNames.ToList();
        var selected = names.FirstOrDefault() ?? "";
        Result = CreateResult(selected);
        Title = "Unhide Sheet";
        Width = 340;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _sheetBox.ItemsSource = names;
        _sheetBox.SelectedItem = selected;
        _sheetBox.SelectionMode = SelectionMode.Single;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = "_Sheet:", Target = _sheetBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _sheetBox.Margin = new Thickness(0, 0, 0, 12);
        _sheetBox.MinHeight = 64;
        stack.Children.Add(_sheetBox);
        stack.Children.Add(DialogButtonRowFactory.Create(Accept, 72));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static UnhideSheetDialogResult CreateResult(string sheetName) => new(sheetName.Trim());

    private void FocusInitialKeyboardTarget()
    {
        _sheetBox.Focus();
        Keyboard.Focus(_sheetBox);
    }

    private void Accept()
    {
        if (_sheetBox.SelectedItem is not string sheetName)
            return;

        Result = CreateResult(sheetName);
        DialogResult = true;
    }
}

