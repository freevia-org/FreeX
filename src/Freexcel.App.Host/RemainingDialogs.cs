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

public enum FillSeriesDirection
{
    Rows,
    Columns
}

public enum FillSeriesType
{
    Linear,
    Growth,
    Date,
    AutoFill
}

public enum FillSeriesDateUnit
{
    Day,
    Weekday,
    Month,
    Year
}

public sealed record FillSeriesStepDialogResult(
    double Step,
    FillSeriesDirection SeriesIn = FillSeriesDirection.Columns,
    FillSeriesType Type = FillSeriesType.Linear,
    FillSeriesDateUnit DateUnit = FillSeriesDateUnit.Day,
    double? StopValue = null);

public sealed class FillSeriesStepDialog : Window
{
    private readonly TextBox _stepBox = new();
    private readonly TextBox _stopBox = new();
    private readonly RadioButton _rowsButton = new() { Content = "_Rows", GroupName = "SeriesIn" };
    private readonly RadioButton _columnsButton = new() { Content = "_Columns", GroupName = "SeriesIn", IsChecked = true };
    private readonly RadioButton _linearButton = new() { Content = "_Linear", GroupName = "SeriesType", IsChecked = true };
    private readonly RadioButton _growthButton = new() { Content = "_Growth", GroupName = "SeriesType" };
    private readonly RadioButton _dateButton = new() { Content = "_Date", GroupName = "SeriesType" };
    private readonly RadioButton _autoFillButton = new() { Content = "_AutoFill", GroupName = "SeriesType" };
    private readonly RadioButton _dayButton = new() { Content = "Da_y", GroupName = "DateUnit", IsChecked = true };
    private readonly RadioButton _weekdayButton = new() { Content = "_Weekday", GroupName = "DateUnit" };
    private readonly RadioButton _monthButton = new() { Content = "_Month", GroupName = "DateUnit" };
    private readonly RadioButton _yearButton = new() { Content = "Y_ear", GroupName = "DateUnit" };

    public FillSeriesStepDialogResult Result { get; private set; } = new(1);

    public FillSeriesStepDialog(double step = 1)
    {
        Result = new FillSeriesStepDialogResult(step);
        Title = "Series";
        Width = 380;
        Height = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _stepBox.Text = step.ToString(CultureInfo.InvariantCulture);
        _stopBox.Text = "";
        Content = CreateSeriesContent();
    }

    public static bool TryCreateResult(string? input, out FillSeriesStepDialogResult result, out string? error)
    {
        result = new FillSeriesStepDialogResult(1);
        error = null;
        if (input is null || !FillSeriesPlanner.TryParseStep(input, out var step))
        {
            error = "Enter a numeric step value.";
            return false;
        }

        result = new FillSeriesStepDialogResult(step);
        return true;
    }

    public static FillSeriesStepDialogResult CreateResult(
        FillSeriesDirection seriesIn,
        FillSeriesType type,
        FillSeriesDateUnit dateUnit,
        string? stepText,
        string? stopText)
    {
        var step = FillSeriesPlanner.TryParseStep(stepText ?? "", out var parsedStep)
            ? parsedStep
            : 1;
        var stopValue = TryParseOptionalFiniteDouble(stopText, out var parsedStop)
            ? parsedStop
            : (double?)null;

        return new FillSeriesStepDialogResult(step, seriesIn, type, dateUnit, stopValue);
    }

    private UIElement CreateSeriesContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Series in", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        stack.Children.Add(CreateHorizontalRow(_rowsButton, _columnsButton));
        stack.Children.Add(new TextBlock { Text = "Type", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
        stack.Children.Add(CreateHorizontalRow(_linearButton, _growthButton, _dateButton, _autoFillButton));
        stack.Children.Add(new TextBlock { Text = "Date unit", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
        stack.Children.Add(CreateHorizontalRow(_dayButton, _weekdayButton, _monthButton, _yearButton));
        stack.Children.Add(CreateLabeledTextBox("Step _value:", _stepBox));
        stack.Children.Add(CreateLabeledTextBox("Stop _value:", _stopBox));
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
    }

    private void Accept()
    {
        if (!TryCreateResult(_stepBox.Text, out var result, out _))
            return;
        Result = CreateResult(
            _rowsButton.IsChecked == true ? FillSeriesDirection.Rows : FillSeriesDirection.Columns,
            SelectedSeriesType(),
            SelectedDateUnit(),
            _stepBox.Text,
            _stopBox.Text);
        DialogResult = true;
    }

    private FillSeriesType SelectedSeriesType() =>
        _growthButton.IsChecked == true ? FillSeriesType.Growth :
        _dateButton.IsChecked == true ? FillSeriesType.Date :
        _autoFillButton.IsChecked == true ? FillSeriesType.AutoFill :
        FillSeriesType.Linear;

    private FillSeriesDateUnit SelectedDateUnit() =>
        _weekdayButton.IsChecked == true ? FillSeriesDateUnit.Weekday :
        _monthButton.IsChecked == true ? FillSeriesDateUnit.Month :
        _yearButton.IsChecked == true ? FillSeriesDateUnit.Year :
        FillSeriesDateUnit.Day;

    private static bool TryParseOptionalFiniteDouble(string? input, out double value)
    {
        value = 0;
        return !string.IsNullOrWhiteSpace(input) &&
               double.TryParse(input.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value);
    }

    private static StackPanel CreateHorizontalRow(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var child in children)
        {
            if (child is Control control)
                control.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(child);
        }

        return row;
    }

    private static Grid CreateLabeledTextBox(string label, TextBox textBox)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.Children.Add(new Label { Content = label, Target = textBox, Padding = new Thickness(0, 3, 8, 0) });
        textBox.Height = 24;
        Grid.SetColumn(textBox, 1);
        grid.Children.Add(textBox);
        return grid;
    }
}

public sealed record ZoomDialogResult(int ZoomPercent, bool FitSelection = false);

public sealed class ZoomDialog : Window
{
    private static readonly int[] ZoomPresets = [200, 100, 75, 50, 25];
    private readonly TextBox _zoomBox = new();
    private readonly RadioButton _customZoomButton = new() { Content = "_Custom:", GroupName = "Zoom", IsChecked = true };
    private readonly RadioButton _fitSelectionButton = new() { Content = "Fit _selection", GroupName = "Zoom" };
    private readonly List<RadioButton> _presetButtons = [];

    public ZoomDialogResult Result { get; private set; }

    public ZoomDialog(int currentZoomPercent)
    {
        Result = new ZoomDialogResult(currentZoomPercent);
        Title = "Zoom";
        Width = 300;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _zoomBox.Text = currentZoomPercent.ToString(CultureInfo.InvariantCulture);
        Content = CreateZoomContent(currentZoomPercent);
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

    public static ZoomDialogResult CreateFitSelectionResult(int currentZoomPercent) =>
        new(currentZoomPercent, FitSelection: true);

    private void Accept()
    {
        if (_fitSelectionButton.IsChecked == true)
        {
            Result = CreateFitSelectionResult(Result.ZoomPercent);
            DialogResult = true;
            return;
        }

        var selectedPreset = _presetButtons
            .Where(button => button.IsChecked == true)
            .Select(button => button.Tag?.ToString())
            .FirstOrDefault();
        var input = selectedPreset ?? _zoomBox.Text;
        if (!TryCreateResult(input, out var result, out _))
            return;
        Result = result;
        DialogResult = true;
    }

    private UIElement CreateZoomContent(int currentZoomPercent)
    {
        var stack = new StackPanel { Margin = new Thickness(12) };
        var group = new GroupBox
        {
            Header = "Magnification",
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var choices = new Grid();
        choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var presets = new StackPanel();
        foreach (var preset in ZoomPresets)
        {
            var button = new RadioButton
            {
                Content = $"{preset}%",
                GroupName = "Zoom",
                Tag = preset,
                IsChecked = preset == currentZoomPercent,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _presetButtons.Add(button);
            presets.Children.Add(button);
        }

        choices.Children.Add(presets);
        var customChoices = new StackPanel();
        _customZoomButton.IsChecked = !ZoomPresets.Contains(currentZoomPercent);
        _fitSelectionButton.Margin = new Thickness(0, 0, 0, 10);
        customChoices.Children.Add(_fitSelectionButton);
        var customRow = new StackPanel { Orientation = Orientation.Horizontal };
        customRow.Children.Add(_customZoomButton);
        _zoomBox.Width = 72;
        _zoomBox.Height = 24;
        customRow.Children.Add(_zoomBox);
        customRow.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
        customChoices.Children.Add(customRow);
        Grid.SetColumn(customChoices, 1);
        choices.Children.Add(customChoices);
        group.Content = choices;
        stack.Children.Add(group);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
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
        _insertRowButton.Checked += (_, _) => RefreshInputStates();
        _insertColumnButton.Checked += (_, _) => RefreshInputStates();
        _resetAllButton.Checked += (_, _) => RefreshInputStates();
        Content = CreateContent();
        RefreshInputStates();
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

    private void RefreshInputStates()
    {
        _rowBreakBox.IsEnabled = _insertRowButton.IsChecked == true;
        _columnBreakBox.IsEnabled = _insertColumnButton.IsChecked == true;
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
    private readonly Button _dataRangePickerButton = new() { Content = "_Select Data Range", Width = 132, ToolTip = "Select Data Range" };
    private readonly Button _locationPickerButton = new() { Content = "Select _Location Range", Width = 152, ToolTip = "Select Location Range" };

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
        _dataRangePickerButton.Click += (_, _) => FocusRangeBox(_dataRangeBox);
        _locationPickerButton.Click += (_, _) => FocusRangeBox(_locationBox);

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = "_Data range:", Target = _dataRangeBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _dataRangeBox.Text = Result.DataRangeText;
        stack.Children.Add(CreateRangePickerRow(_dataRangeBox, _dataRangePickerButton));
        stack.Children.Add(new Label { Content = "_Location:", Target = _locationBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _locationBox.Text = Result.LocationText;
        stack.Children.Add(CreateRangePickerRow(_locationBox, _locationPickerButton));
        stack.Children.Add(new Label { Content = "Sparkline _type:", Target = _kindBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
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

    private static void FocusRangeBox(TextBox textBox)
    {
        textBox.Focus();
        textBox.SelectAll();
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

public enum SpellCheckDialogAction
{
    Replace,
    ReplaceAll,
    Ignore,
    IgnoreAll,
    Add
}

public sealed record SpellCheckDialogResult(SpellCheckDialogAction Action, string? Replacement);

public sealed class SpellCheckDialog : Window
{
    private readonly TextBox _notInDictionaryBox = new();
    private readonly TextBox _replacementBox = new();
    private readonly ListBox _suggestionsBox = new();

    public SpellCheckDialogResult Result { get; private set; }

    public SpellCheckDialog(string word, string suggestion)
    {
        Result = CreateReplaceResult(word, suggestion);
        Title = "Spelling";
        Width = 480;
        Height = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _notInDictionaryBox.Text = word;
        _notInDictionaryBox.IsReadOnly = true;
        _notInDictionaryBox.Height = 56;
        _notInDictionaryBox.TextWrapping = TextWrapping.Wrap;
        _replacementBox.Text = suggestion;
        if (!string.IsNullOrWhiteSpace(suggestion))
        {
            _suggestionsBox.Items.Add(suggestion);
            _suggestionsBox.SelectedIndex = 0;
        }

        _suggestionsBox.Height = 76;
        _suggestionsBox.SelectionChanged += (_, _) =>
        {
            if (_suggestionsBox.SelectedItem is string selected)
                _replacementBox.Text = selected;
        };

        Content = CreateSpellCheckContent(word);
    }

    public static SpellCheckDialogResult CreateReplaceResult(string word, string replacement) =>
        new(SpellCheckDialogAction.Replace, string.IsNullOrWhiteSpace(replacement) ? word : replacement.Trim());

    public static SpellCheckDialogResult CreateReplaceAllResult(string word, string replacement) =>
        new(SpellCheckDialogAction.ReplaceAll, string.IsNullOrWhiteSpace(replacement) ? word : replacement.Trim());

    public static SpellCheckDialogResult CreateIgnoreResult() =>
        new(SpellCheckDialogAction.Ignore, null);

    public static SpellCheckDialogResult CreateIgnoreAllResult() =>
        new(SpellCheckDialogAction.IgnoreAll, null);

    public static SpellCheckDialogResult CreateAddResult(string word) =>
        new(SpellCheckDialogAction.Add, word.Trim());

    private void Accept(SpellCheckDialogResult result)
    {
        Result = result;
        DialogResult = true;
    }

    private UIElement CreateSpellCheckContent(string word)
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var fields = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        fields.Children.Add(new Label { Content = "Not in _Dictionary:", Target = _notInDictionaryBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        fields.Children.Add(_notInDictionaryBox);
        fields.Children.Add(new Label { Content = "_Suggestions:", Target = _suggestionsBox, Padding = new Thickness(0), Margin = new Thickness(0, 10, 0, 4) });
        fields.Children.Add(_suggestionsBox);
        fields.Children.Add(new Label { Content = "_Change to:", Target = _replacementBox, Padding = new Thickness(0), Margin = new Thickness(0, 10, 0, 4) });
        fields.Children.Add(_replacementBox);
        root.Children.Add(fields);

        var actionButtons = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = "_Ignore", Width = 90 }, (_, _) => Accept(CreateIgnoreResult())));
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = "Ignore _All", Width = 90 }, (_, _) => Accept(CreateIgnoreAllResult())));
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = "_Change", Width = 90 }, (_, _) => Accept(CreateReplaceResult(word, _replacementBox.Text))));
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = "Change A_ll", Width = 90 }, (_, _) => Accept(CreateReplaceAllResult(word, _replacementBox.Text))));
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = "_Add", Width = 90 }, (_, _) => Accept(CreateAddResult(word))));
        actionButtons.Children.Add(new Button { Content = "_Cancel", Width = 90, IsCancel = true, Margin = new Thickness(0, 8, 0, 0) });
        Grid.SetColumn(actionButtons, 1);
        root.Children.Add(actionButtons);
        return root;
    }

    private static Button CreateSpellingButton(Button button, RoutedEventHandler click)
    {
        button.Margin = new Thickness(0, 0, 0, 6);
        button.Click += click;
        return button;
    }
}
