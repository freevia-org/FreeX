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
        if (!double.TryParse(input?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var height) || height <= 0)
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
        if (!double.TryParse(input?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var width) || width <= 0)
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

public sealed record FillSeriesStepDialogResult(double Step);

public sealed class FillSeriesStepDialog : Window
{
    private readonly TextBox _stepBox = new();

    public FillSeriesStepDialogResult Result { get; private set; } = new(1);

    public FillSeriesStepDialog(double step = 1)
    {
        Result = new FillSeriesStepDialogResult(step);
        Title = "Series";
        Width = 320;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _stepBox.Text = step.ToString(CultureInfo.InvariantCulture);
        Content = ObjectSizeDialog.CreateSingleInputContent("Step value:", _stepBox, Accept);
    }

    public static bool TryCreateResult(string? input, out FillSeriesStepDialogResult result, out string? error)
    {
        result = new FillSeriesStepDialogResult(1);
        error = null;
        if (!double.TryParse(input?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var step))
        {
            error = "Enter a numeric step value.";
            return false;
        }

        result = new FillSeriesStepDialogResult(step);
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_stepBox.Text, out var result, out _))
            return;
        Result = result;
        DialogResult = true;
    }
}

public sealed record ZoomDialogResult(int ZoomPercent);

public sealed class ZoomDialog : Window
{
    private readonly TextBox _zoomBox = new();

    public ZoomDialogResult Result { get; private set; }

    public ZoomDialog(int currentZoomPercent)
    {
        Result = new ZoomDialogResult(currentZoomPercent);
        Title = "Zoom";
        Width = 300;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _zoomBox.Text = currentZoomPercent.ToString(CultureInfo.InvariantCulture);
        Content = ObjectSizeDialog.CreateSingleInputContent("Zoom percent:", _zoomBox, Accept);
    }

    public static bool TryCreateResult(string? input, out ZoomDialogResult result, out string? error)
    {
        result = new ZoomDialogResult(100);
        error = null;
        if (!Freexcel.App.UI.ZoomLevelMapper.TryParseZoomPercent(input, out var zoomPercent))
        {
            error = "Zoom must be between 10% and 400%.";
            return false;
        }

        result = new ZoomDialogResult((int)Math.Round(zoomPercent));
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_zoomBox.Text, out var result, out _))
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
    private readonly TextBox _breakBox = new();

    public PageBreakDialogResult Result { get; private set; } = CreateClearResult();

    public PageBreakDialog(string defaultValue)
    {
        Title = "Page Breaks";
        Width = 340;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _breakBox.Text = defaultValue;
        Content = ObjectSizeDialog.CreateSingleInputContent("Page break:", _breakBox, Accept);
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
        if (!TryCreateResult(_breakBox.Text, out var result))
            return;
        Result = result;
        DialogResult = true;
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

public enum SparklineKindChoice
{
    Line,
    Column,
    WinLoss
}

public sealed record SparklineDialogResult(string DataRangeText, string LocationText, SparklineKindChoice Kind);

public sealed class SparklineDialog : Window
{
    private readonly TextBox _dataRangeBox = new();
    private readonly TextBox _locationBox = new();
    private readonly ComboBox _kindBox = new();

    public SparklineDialogResult Result { get; private set; }

    public SparklineDialog(string dataRangeText, string locationText, SparklineKindChoice kind)
    {
        Result = CreateResult(dataRangeText, locationText, kind);
        Title = "Insert Sparkline";
        Width = 380;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Data range", Margin = new Thickness(0, 0, 0, 4) });
        _dataRangeBox.Text = Result.DataRangeText;
        _dataRangeBox.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(_dataRangeBox);
        stack.Children.Add(new TextBlock { Text = "Location", Margin = new Thickness(0, 0, 0, 4) });
        _locationBox.Text = Result.LocationText;
        _locationBox.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(_locationBox);
        _kindBox.ItemsSource = Enum.GetValues<SparklineKindChoice>();
        _kindBox.SelectedItem = kind;
        _kindBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_kindBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        Content = stack;
    }

    public static SparklineDialogResult CreateResult(string dataRangeText, string locationText, SparklineKindChoice kind) =>
        new(dataRangeText.Trim(), locationText.Trim(), kind);

    private void Accept()
    {
        Result = CreateResult(
            _dataRangeBox.Text,
            _locationBox.Text,
            _kindBox.SelectedItem is SparklineKindChoice kind ? kind : SparklineKindChoice.Line);
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
    private readonly ComboBox _sheetBox = new();

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
        _sheetBox.IsEditable = true;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Sheet:", Margin = new Thickness(0, 0, 0, 4) });
        _sheetBox.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(_sheetBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        Content = stack;
    }

    public static UnhideSheetDialogResult CreateResult(string sheetName) => new(sheetName.Trim());

    private void Accept()
    {
        Result = CreateResult(_sheetBox.Text);
        DialogResult = true;
    }
}

public enum SpellCheckDialogAction
{
    Replace,
    ReplaceAll,
    Ignore
}

public sealed record SpellCheckDialogResult(SpellCheckDialogAction Action, string? Replacement);

public sealed class SpellCheckDialog : Window
{
    private readonly TextBox _replacementBox = new();
    private readonly RadioButton _replaceButton = new() { Content = "Replace", IsChecked = true };
    private readonly RadioButton _replaceAllButton = new() { Content = "Replace all known corrections" };
    private readonly RadioButton _ignoreButton = new() { Content = "Ignore" };

    public SpellCheckDialogResult Result { get; private set; }

    public SpellCheckDialog(string word, string suggestion)
    {
        Result = CreateReplaceResult(word, suggestion);
        Title = "Spelling";
        Width = 380;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = $"Not in dictionary: {word}", Margin = new Thickness(0, 0, 0, 8) });
        _replacementBox.Text = suggestion;
        _replacementBox.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(_replacementBox);
        stack.Children.Add(_replaceButton);
        stack.Children.Add(_replaceAllButton);
        stack.Children.Add(_ignoreButton);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        Content = stack;
    }

    public static SpellCheckDialogResult CreateReplaceResult(string word, string replacement) =>
        new(SpellCheckDialogAction.Replace, string.IsNullOrWhiteSpace(replacement) ? word : replacement.Trim());

    public static SpellCheckDialogResult CreateReplaceAllResult() =>
        new(SpellCheckDialogAction.ReplaceAll, null);

    public static SpellCheckDialogResult CreateIgnoreResult() =>
        new(SpellCheckDialogAction.Ignore, null);

    private void Accept()
    {
        Result = _replaceAllButton.IsChecked == true
            ? CreateReplaceAllResult()
            : _ignoreButton.IsChecked == true
                ? CreateIgnoreResult()
                : CreateReplaceResult(_replacementBox.Text, _replacementBox.Text);
        DialogResult = true;
    }
}
