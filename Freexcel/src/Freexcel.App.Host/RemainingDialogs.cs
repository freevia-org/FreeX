using System.Globalization;
using System.Windows;
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
        Content = ObjectSizeDialog.CreateSingleInputContent("Format cells greater than:", _thresholdBox, Accept);
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
        Content = ObjectSizeDialog.CreateSingleInputContent("Row height:", _heightBox, Accept);
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
        Content = ObjectSizeDialog.CreateSingleInputContent("Column width:", _widthBox, Accept);
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

public enum PageBreakDialogAction
{
    Clear,
    AddRow,
    AddColumn
}

public sealed record PageBreakDialogResult(PageBreakDialogAction Action, uint? RowBreak, uint? ColumnBreak);

public sealed class PageBreakDialog : Window
{
    private readonly RadioButton _insertRowButton = new() { Content = "Insert _row page break", IsChecked = true };
    private readonly RadioButton _insertColumnButton = new() { Content = "Insert _column page break" };
    private readonly RadioButton _resetAllButton = new() { Content = "_Reset all page breaks" };
    private readonly TextBox _rowBreakBox = new();
    private readonly TextBox _columnBreakBox = new();

    public PageBreakDialogResult Result { get; private set; } = CreateClearResult();

    public PageBreakDialog(string defaultValue)
    {
        Title = "Page Breaks";
        Width = 360;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SeedDefault(defaultValue);
        Content = CreateContent();
    }

    public static PageBreakDialogResult CreateClearResult() =>
        new(PageBreakDialogAction.Clear, null, null);

    public static bool TryCreateResult(string input, out PageBreakDialogResult result)
    {
        result = CreateClearResult();
        var trimmed = input.Trim();
        if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
            return true;

        if (PageLayoutInputParser.TryParseBreakInput(trimmed, "row", out var rowBreak))
        {
            result = new PageBreakDialogResult(PageBreakDialogAction.AddRow, rowBreak, null);
            return true;
        }

        if (PageLayoutInputParser.TryParseBreakInput(trimmed, "col", out var columnBreak) ||
            PageLayoutInputParser.TryParseBreakInput(trimmed, "column", out columnBreak))
        {
            result = new PageBreakDialogResult(PageBreakDialogAction.AddColumn, null, columnBreak);
            return true;
        }

        return false;
    }

    private void Accept()
    {
        PageBreakDialogResult result;
        if (_resetAllButton.IsChecked == true)
            result = CreateClearResult();
        else if (_insertColumnButton.IsChecked == true)
        {
            if (!uint.TryParse(_columnBreakBox.Text.Trim(), out var columnBreak))
                return;
            result = new PageBreakDialogResult(PageBreakDialogAction.AddColumn, null, columnBreak);
        }
        else
        {
            if (!uint.TryParse(_rowBreakBox.Text.Trim(), out var rowBreak))
                return;
            result = new PageBreakDialogResult(PageBreakDialogAction.AddRow, rowBreak, null);
        }

        Result = result;
        DialogResult = true;
    }

    private void SeedDefault(string defaultValue)
    {
        if (!TryCreateResult(defaultValue, out var result))
            result = new PageBreakDialogResult(PageBreakDialogAction.AddRow, 2, null);

        _insertRowButton.IsChecked = result.Action == PageBreakDialogAction.AddRow;
        _insertColumnButton.IsChecked = result.Action == PageBreakDialogAction.AddColumn;
        _resetAllButton.IsChecked = result.Action == PageBreakDialogAction.Clear;
        _rowBreakBox.Text = (result.RowBreak ?? 2).ToString(CultureInfo.InvariantCulture);
        _columnBreakBox.Text = (result.ColumnBreak ?? 2).ToString(CultureInfo.InvariantCulture);
    }

    private UIElement CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(_insertRowButton);
        stack.Children.Add(CreateNumberRow("_Row:", _rowBreakBox));
        stack.Children.Add(_insertColumnButton);
        stack.Children.Add(CreateNumberRow("_Column:", _columnBreakBox));
        _resetAllButton.Margin = new Thickness(0, 4, 0, 12);
        stack.Children.Add(_resetAllButton);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
    }

    private static StackPanel CreateNumberRow(string label, TextBox box)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 2, 0, 8) };
        row.Children.Add(new Label { Content = label, Target = box, Width = 72, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center });
        box.Width = 96;
        row.Children.Add(box);
        return row;
    }
}

public sealed record ForecastSheetDialogResult(uint Periods);

public sealed class ForecastSheetDialog : Window
{
    private readonly TextBox _periodsBox = new();

    public ForecastSheetDialogResult Result { get; private set; } = new(3);

    public ForecastSheetDialog(uint periods = 3)
    {
        Result = new ForecastSheetDialogResult(periods);
        Title = "Forecast Sheet";
        Width = 320;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _periodsBox.Text = periods.ToString(CultureInfo.InvariantCulture);
        Content = ObjectSizeDialog.CreateSingleInputContent("Forecast periods:", _periodsBox, Accept);
    }

    public static bool TryCreateResult(string input, out ForecastSheetDialogResult result, out string? error)
    {
        result = new ForecastSheetDialogResult(3);
        error = null;
        if (!ForecastSheetInputParser.TryParsePeriods(input, out var periods))
        {
            error = "Enter a positive whole number of forecast periods.";
            return false;
        }

        result = new ForecastSheetDialogResult(periods);
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_periodsBox.Text, out var result, out _))
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
        Content = ObjectSizeDialog.CreateSingleInputContent("Sheet name:", _nameBox, () =>
        {
            Result = CreateResult(_nameBox.Text);
            DialogResult = true;
        });
    }

    public static SheetNameDialogResult CreateResult(string sheetName) => new(sheetName.Trim());
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
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        Content = stack;
    }

    public static UnhideSheetDialogResult CreateResult(string sheetName) => new(sheetName.Trim());

    private void Accept()
    {
        if (_sheetBox.SelectedItem is not string sheetName)
            return;

        Result = CreateResult(sheetName);
        DialogResult = true;
    }
}

