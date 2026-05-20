using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using static Freexcel.App.Host.ChartDialogHelpers;

namespace Freexcel.App.Host;

public sealed record ChartTypePickerOption(ChartType Type, string DisplayName, bool IsRecommended = false);

public static class ChartTypePickerPlanner
{
    private static readonly ChartTypePickerOption[] Options =
    [
        new(ChartType.Column, "Clustered Column", true),
        new(ChartType.StackedColumn, "Stacked Column"),
        new(ChartType.PercentStackedColumn, "100% Stacked Column"),
        new(ChartType.Line, "Line", true),
        new(ChartType.Pie, "Pie", true),
        new(ChartType.Doughnut, "Doughnut"),
        new(ChartType.Bar, "Clustered Bar", true),
        new(ChartType.StackedBar, "Stacked Bar"),
        new(ChartType.PercentStackedBar, "100% Stacked Bar"),
        new(ChartType.Scatter, "Scatter", true),
        new(ChartType.Bubble, "Bubble"),
        new(ChartType.Area, "Area"),
        new(ChartType.Radar, "Radar"),
        new(ChartType.Stock, "Stock")
    ];

    public static IReadOnlyList<ChartTypePickerOption> GetSupportedOptions() =>
        Options.Where(option => ChartTypeSupport.IsRenderable(option.Type)).ToList();

    public static IReadOnlyList<ChartTypePickerOption> GetRecommendedOptions() =>
        new[]
        {
            ChartType.Column,
            ChartType.Line,
            ChartType.Bar,
            ChartType.Pie,
            ChartType.Scatter
        }
        .Select(type => Options.Single(option => option.Type == type))
        .Where(option => option.IsRecommended && ChartTypeSupport.IsRenderable(option.Type))
        .ToList();
}

public sealed record InsertChartDialogResult(ChartType ChartType, bool UseRecommendedLayout);

public sealed class InsertChartDialog : Window
{
    private readonly ComboBox _chartTypeBox = new();
    private readonly CheckBox _recommendedBox = new() { Content = "Use recommended layout" };

    public InsertChartDialogResult Result { get; private set; } = CreateRecommendedResult();

    public InsertChartDialog()
    {
        Title = "Insert Chart";
        Width = 360;
        Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Chart type", Margin = new Thickness(0, 0, 0, 4) });
        _chartTypeBox.ItemsSource = ChartTypePickerPlanner.GetSupportedOptions();
        _chartTypeBox.DisplayMemberPath = nameof(ChartTypePickerOption.DisplayName);
        _chartTypeBox.SelectedIndex = 0;
        _chartTypeBox.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(_chartTypeBox);

        _recommendedBox.IsChecked = true;
        _recommendedBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_recommendedBox);
        stack.Children.Add(CreateButtonRow(Accept));
        Content = stack;
    }

    public static InsertChartDialogResult CreateResult(ChartType chartType) =>
        new(chartType, UseRecommendedLayout: false);

    public static InsertChartDialogResult CreateRecommendedResult() =>
        new(ChartTypePickerPlanner.GetRecommendedOptions().First().Type, UseRecommendedLayout: true);

    private void Accept()
    {
        var selected = _chartTypeBox.SelectedItem is ChartTypePickerOption option
            ? option.Type
            : ChartType.Column;
        Result = new InsertChartDialogResult(selected, _recommendedBox.IsChecked == true);
        DialogResult = true;
    }

    internal static StackPanel CreateButtonRow(Action accept) =>
        DialogButtonRowFactory.Create(accept, buttonWidth: 76);
}

public sealed record ChangeChartTypeDialogResult(ChartType ChartType);

public sealed class ChangeChartTypeDialog : Window
{
    private readonly ComboBox _chartTypeBox = new();

    public ChartType SelectedChartType { get; private set; }
    public ChangeChartTypeDialogResult Result { get; private set; }

    public ChangeChartTypeDialog(ChartType currentType)
    {
        SelectedChartType = currentType;
        Result = CreateResult(currentType);
        Title = "Change Chart Type";
        Width = 340;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        _chartTypeBox.ItemsSource = ChartTypePickerPlanner.GetSupportedOptions();
        _chartTypeBox.DisplayMemberPath = nameof(ChartTypePickerOption.DisplayName);
        _chartTypeBox.SelectedItem = ChartTypePickerPlanner.GetSupportedOptions()
            .FirstOrDefault(option => option.Type == currentType);
        stack.Children.Add(_chartTypeBox);
        stack.Children.Add(CreateButtonRow());
        Content = stack;
    }

    public static ChangeChartTypeDialogResult CreateResult(ChartType chartType) => new(chartType);

    private StackPanel CreateButtonRow() => InsertChartDialog.CreateButtonRow(() =>
    {
        if (_chartTypeBox.SelectedItem is ChartTypePickerOption option)
            SelectedChartType = option.Type;
        Result = CreateResult(SelectedChartType);
        DialogResult = true;
    });
}

public sealed record ChartStyleDialogResult(int? ChartStyleId);

public sealed class ChartStyleDialog : Window
{
    private readonly ComboBox _styleBox = new();

    public ChartStyleDialogResult Result { get; private set; }

    public ChartStyleDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "Chart Styles";
        Width = 340;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var options = GetStyleOptions();
        _styleBox.ItemsSource = options;
        _styleBox.DisplayMemberPath = nameof(ChartStyleOption.DisplayName);
        _styleBox.SelectedItem = options.FirstOrDefault(option => option.StyleId == Result.ChartStyleId) ?? options[0];
        _styleBox.Margin = new Thickness(0, 0, 0, 16);

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Style", Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(_styleBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        Content = stack;
    }

    public static ChartStyleDialogResult FromChart(ChartModel chart) =>
        new(NormalizeStyleId(chart.ChartStyleId));

    public static ChartStyleDialogResult CreateResult(int? chartStyleId) =>
        new(NormalizeStyleId(chartStyleId));

    public static IReadOnlyList<ChartStyleOption> GetStyleOptions() =>
    [
        new(null, "Automatic"),
        new(2, "Style 2 - Clean"),
        new(4, "Style 4 - Colorful"),
        new(10, "Style 10 - Accent"),
        new(26, "Style 26 - Strong"),
        new(42, "Style 42 - Pivot")
    ];

    private void Accept()
    {
        Result = _styleBox.SelectedItem is ChartStyleOption option
            ? CreateResult(option.StyleId)
            : CreateResult(null);
        DialogResult = true;
    }

    private static int? NormalizeStyleId(int? value)
    {
        if (value is null)
            return null;

        return Math.Clamp(value.Value, 1, 48);
    }
}

public sealed record ChartStyleOption(int? StyleId, string DisplayName);

public enum MoveChartTargetKind
{
    ObjectInSheet,
    NewChartSheet
}

public sealed record MoveChartDialogResult(MoveChartTargetKind TargetKind, string TargetName);

public sealed class MoveChartDialog : Window
{
    private readonly RadioButton _objectInSheet = new() { Content = "Object in sheet", IsChecked = true };
    private readonly TextBox _targetBox = new();

    public MoveChartDialogResult Result { get; private set; }

    public MoveChartDialog(string currentSheetName)
    {
        Result = CreateObjectResult(currentSheetName);
        Title = "Move Chart";
        Width = 340;
        Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        _targetBox.Text = currentSheetName;
        stack.Children.Add(_objectInSheet);
        stack.Children.Add(new RadioButton { Content = "New chart sheet", Margin = new Thickness(0, 4, 0, 8) });
        stack.Children.Add(_targetBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        Content = stack;
    }

    public static MoveChartDialogResult CreateObjectResult(string? sheetName) =>
        new(MoveChartTargetKind.ObjectInSheet, RequireTargetName(sheetName));

    public static MoveChartDialogResult CreateNewSheetResult(string? sheetName) =>
        new(MoveChartTargetKind.NewChartSheet, RequireTargetName(sheetName));

    private void Accept()
    {
        Result = _objectInSheet.IsChecked == true
            ? CreateObjectResult(_targetBox.Text)
            : CreateNewSheetResult(_targetBox.Text);
        DialogResult = true;
    }

    private static string RequireTargetName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Target name is required.", nameof(name));
        return name.Trim();
    }
}

public sealed record SelectDataSourceDialogResult(string SourceRangeText, bool FirstColumnIsCategories);

public sealed class SelectDataSourceDialog : Window
{
    private readonly TextBox _rangeBox = new();
    private readonly CheckBox _firstColumnCategoriesBox = new() { Content = "First column contains category labels" };

    public SelectDataSourceDialogResult Result { get; private set; }

    public SelectDataSourceDialog(string sourceRangeText, bool firstColumnIsCategories = true)
    {
        Result = CreateResult(sourceRangeText, firstColumnIsCategories);
        Title = "Select Data Source";
        Width = 420;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Chart data range", Margin = new Thickness(0, 0, 0, 4) });
        _rangeBox.Text = Result.SourceRangeText;
        stack.Children.Add(_rangeBox);
        _firstColumnCategoriesBox.IsChecked = firstColumnIsCategories;
        _firstColumnCategoriesBox.Margin = new Thickness(0, 10, 0, 16);
        stack.Children.Add(_firstColumnCategoriesBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(() =>
        {
            Result = CreateResult(_rangeBox.Text, _firstColumnCategoriesBox.IsChecked == true);
            DialogResult = true;
        }));
        Content = stack;
    }

    public static SelectDataSourceDialogResult CreateResult(string sourceRangeText, bool firstColumnIsCategories) =>
        new(sourceRangeText.Trim(), firstColumnIsCategories);
}

public sealed record ChartDataLabelsDialogResult(
    bool ShowDataLabels,
    ChartDataLabelPosition Position,
    bool ShowCategoryName,
    bool ShowSeriesName,
    bool ShowPercentage,
    ChartDataLabelSeparator Separator,
    ChartDataLabelNumberFormat NumberFormat,
    bool ShowCallouts,
    CellColor? FillColor,
    CellColor? BorderColor,
    CellColor? TextColor,
    double BorderThickness,
    double FontSize,
    double Angle)
{
    public ChartLayoutOptions ToOptions() => new(
        ShowDataLabels: ShowDataLabels,
        DataLabelPosition: Position,
        ShowDataLabelCategoryName: ShowCategoryName,
        ShowDataLabelSeriesName: ShowSeriesName,
        ShowDataLabelPercentage: ShowPercentage,
        DataLabelSeparator: Separator,
        DataLabelNumberFormat: NumberFormat,
        ShowDataLabelCallouts: ShowCallouts,
        DataLabelFillColor: FillColor,
        DataLabelBorderColor: BorderColor,
        DataLabelTextColor: TextColor,
        DataLabelBorderThickness: BorderThickness,
        DataLabelFontSize: FontSize,
        DataLabelAngle: Angle);
}

public sealed class ChartDataLabelsDialog : Window
{
    private readonly CheckBox _showBox = new() { Content = "Show data labels" };
    private readonly CheckBox _categoryBox = new() { Content = "Category name" };
    private readonly CheckBox _seriesBox = new() { Content = "Series name" };
    private readonly CheckBox _percentageBox = new() { Content = "Percentage" };
    private readonly CheckBox _calloutsBox = new() { Content = "Data label callouts" };
    private readonly ComboBox _positionBox = new();
    private readonly ComboBox _separatorBox = new();
    private readonly ComboBox _numberFormatBox = new();
    private readonly TextBox _fillBox = new();
    private readonly TextBox _borderBox = new();
    private readonly TextBox _textBox = new();
    private readonly TextBox _borderThicknessBox = new();
    private readonly TextBox _fontSizeBox = new();
    private readonly TextBox _angleBox = new();

    public ChartDataLabelsDialogResult Result { get; private set; }

    public ChartDataLabelsDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "Format Data Labels";
        Width = 420;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
    }

    public static ChartDataLabelsDialogResult FromChart(ChartModel chart) => CreateResult(
        chart.ShowDataLabels,
        chart.DataLabelPosition,
        chart.ShowDataLabelCategoryName,
        chart.ShowDataLabelSeriesName,
        chart.ShowDataLabelPercentage,
        chart.DataLabelSeparator,
        chart.DataLabelNumberFormat,
        chart.ShowDataLabelCallouts,
        chart.DataLabelFillColor,
        chart.DataLabelBorderColor,
        chart.DataLabelTextColor,
        chart.DataLabelBorderThickness,
        chart.DataLabelFontSize,
        chart.DataLabelAngle);

    public static ChartDataLabelsDialogResult CreateResult(
        bool showDataLabels,
        ChartDataLabelPosition position,
        bool showCategoryName,
        bool showSeriesName,
        bool showPercentage,
        ChartDataLabelSeparator separator,
        ChartDataLabelNumberFormat numberFormat,
        bool showCallouts,
        CellColor? fillColor,
        CellColor? borderColor,
        CellColor? textColor,
        double borderThickness,
        double fontSize,
        double angle) =>
        new(showDataLabels, position, showCategoryName, showSeriesName, showPercentage, separator, numberFormat,
            showCallouts, fillColor, borderColor, textColor, borderThickness, fontSize, angle);

    private StackPanel CreateContent()
    {
        var stack = ChartDialogHelpers.DialogStack();
        ChartDialogHelpers.AddCheck(stack, _showBox);
        ChartDialogHelpers.AddCombo(stack, "Position", _positionBox, Enum.GetValues<ChartDataLabelPosition>());
        ChartDialogHelpers.AddCheck(stack, _categoryBox);
        ChartDialogHelpers.AddCheck(stack, _seriesBox);
        ChartDialogHelpers.AddCheck(stack, _percentageBox);
        ChartDialogHelpers.AddCombo(stack, "Separator", _separatorBox, Enum.GetValues<ChartDataLabelSeparator>());
        ChartDialogHelpers.AddCombo(stack, "Number format", _numberFormatBox, Enum.GetValues<ChartDataLabelNumberFormat>());
        ChartDialogHelpers.AddCheck(stack, _calloutsBox);
        ChartDialogHelpers.AddText(stack, "Fill color", _fillBox);
        ChartDialogHelpers.AddText(stack, "Border color", _borderBox);
        ChartDialogHelpers.AddText(stack, "Text color", _textBox);
        ChartDialogHelpers.AddText(stack, "Border thickness", _borderThicknessBox);
        ChartDialogHelpers.AddText(stack, "Font size", _fontSizeBox);
        ChartDialogHelpers.AddText(stack, "Text angle", _angleBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
    }

    private void Load(ChartDataLabelsDialogResult result)
    {
        _showBox.IsChecked = result.ShowDataLabels;
        _positionBox.SelectedItem = result.Position;
        _categoryBox.IsChecked = result.ShowCategoryName;
        _seriesBox.IsChecked = result.ShowSeriesName;
        _percentageBox.IsChecked = result.ShowPercentage;
        _separatorBox.SelectedItem = result.Separator;
        _numberFormatBox.SelectedItem = result.NumberFormat;
        _calloutsBox.IsChecked = result.ShowCallouts;
        _fillBox.Text = ChartDialogHelpers.FormatColor(result.FillColor);
        _borderBox.Text = ChartDialogHelpers.FormatColor(result.BorderColor);
        _textBox.Text = ChartDialogHelpers.FormatColor(result.TextColor);
        _borderThicknessBox.Text = result.BorderThickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _fontSizeBox.Text = result.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _angleBox.Text = result.Angle.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Accept()
    {
        Result = CreateResult(
            _showBox.IsChecked == true,
            ChartDialogHelpers.Selected(_positionBox, ChartDataLabelPosition.BestFit),
            _categoryBox.IsChecked == true,
            _seriesBox.IsChecked == true,
            _percentageBox.IsChecked == true,
            ChartDialogHelpers.Selected(_separatorBox, ChartDataLabelSeparator.Comma),
            ChartDialogHelpers.Selected(_numberFormatBox, ChartDataLabelNumberFormat.General),
            _calloutsBox.IsChecked == true,
            ChartDialogHelpers.ParseColor(_fillBox.Text),
            ChartDialogHelpers.ParseColor(_borderBox.Text),
            ChartDialogHelpers.ParseColor(_textBox.Text),
            ChartDialogHelpers.ParseDouble(_borderThicknessBox.Text, 0),
            ChartDialogHelpers.ParseDouble(_fontSizeBox.Text, 11),
            ChartDialogHelpers.ParseDouble(_angleBox.Text, 0));
        DialogResult = true;
    }
}

public sealed record ChartTrendlineOptionsDialogResult(
    bool ShowTrendline,
    ChartTrendlineType Type,
    int Period,
    int Order,
    bool ShowEquation,
    bool ShowRSquared,
    CellColor? Color,
    double Thickness,
    ChartLineDashStyle DashStyle)
{
    public ChartLayoutOptions ToOptions() => new(
        ShowLinearTrendline: ShowTrendline,
        TrendlineType: Type,
        TrendlinePeriod: Period,
        TrendlineOrder: Order,
        ShowTrendlineEquation: ShowEquation,
        ShowTrendlineRSquared: ShowRSquared,
        TrendlineColor: Color,
        TrendlineThickness: Thickness,
        TrendlineDashStyle: DashStyle);
}

public sealed class ChartTrendlineOptionsDialog : Window
{
    private readonly CheckBox _showBox = new() { Content = "Show trendline" };
    private readonly CheckBox _equationBox = new() { Content = "Display equation" };
    private readonly CheckBox _rSquaredBox = new() { Content = "Display R-squared value" };
    private readonly ComboBox _typeBox = new();
    private readonly ComboBox _dashBox = new();
    private readonly TextBox _periodBox = new();
    private readonly TextBox _orderBox = new();
    private readonly TextBox _colorBox = new();
    private readonly TextBox _thicknessBox = new();

    public ChartTrendlineOptionsDialogResult Result { get; private set; }

    public ChartTrendlineOptionsDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "Format Trendline";
        Width = 380;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
    }

    public static ChartTrendlineOptionsDialogResult FromChart(ChartModel chart) => CreateResult(
        chart.ShowLinearTrendline,
        chart.TrendlineType,
        chart.TrendlinePeriod,
        chart.TrendlineOrder,
        chart.ShowTrendlineEquation,
        chart.ShowTrendlineRSquared,
        chart.TrendlineColor,
        chart.TrendlineThickness,
        chart.TrendlineDashStyle);

    public static ChartTrendlineOptionsDialogResult CreateResult(
        bool showTrendline,
        ChartTrendlineType type,
        int period,
        int order,
        bool showEquation,
        bool showRSquared,
        CellColor? color,
        double thickness,
        ChartLineDashStyle dashStyle) =>
        new(showTrendline, type, Math.Clamp(period, 2, 255), Math.Clamp(order, 2, 6), showEquation, showRSquared, color, thickness, dashStyle);

    private StackPanel CreateContent()
    {
        var stack = ChartDialogHelpers.DialogStack();
        ChartDialogHelpers.AddCheck(stack, _showBox);
        ChartDialogHelpers.AddCombo(stack, "Type", _typeBox, Enum.GetValues<ChartTrendlineType>());
        ChartDialogHelpers.AddText(stack, "Moving average period", _periodBox);
        ChartDialogHelpers.AddText(stack, "Polynomial order", _orderBox);
        ChartDialogHelpers.AddCheck(stack, _equationBox);
        ChartDialogHelpers.AddCheck(stack, _rSquaredBox);
        ChartDialogHelpers.AddText(stack, "Line color", _colorBox);
        ChartDialogHelpers.AddText(stack, "Line width", _thicknessBox);
        ChartDialogHelpers.AddCombo(stack, "Dash style", _dashBox, Enum.GetValues<ChartLineDashStyle>());
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
    }

    private void Load(ChartTrendlineOptionsDialogResult result)
    {
        _showBox.IsChecked = result.ShowTrendline;
        _typeBox.SelectedItem = result.Type;
        _periodBox.Text = result.Period.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _orderBox.Text = result.Order.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _equationBox.IsChecked = result.ShowEquation;
        _rSquaredBox.IsChecked = result.ShowRSquared;
        _colorBox.Text = ChartDialogHelpers.FormatColor(result.Color);
        _thicknessBox.Text = result.Thickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _dashBox.SelectedItem = result.DashStyle;
    }

    private void Accept()
    {
        Result = CreateResult(
            _showBox.IsChecked == true,
            ChartDialogHelpers.Selected(_typeBox, ChartTrendlineType.Linear),
            (int)ChartDialogHelpers.ParseDouble(_periodBox.Text, 2),
            (int)ChartDialogHelpers.ParseDouble(_orderBox.Text, 2),
            _equationBox.IsChecked == true,
            _rSquaredBox.IsChecked == true,
            ChartDialogHelpers.ParseColor(_colorBox.Text),
            ChartDialogHelpers.ParseDouble(_thicknessBox.Text, 1.5),
            ChartDialogHelpers.Selected(_dashBox, ChartLineDashStyle.Solid));
        DialogResult = true;
    }
}

public sealed record ChartAxisFormatDialogResult(
    bool UseXAxis,
    double? Minimum,
    double? Maximum,
    double? MajorUnit,
    double? MinorUnit,
    bool LogScale,
    ChartDataLabelNumberFormat NumberFormat,
    bool ShowMajorGridlines,
    bool ShowMinorGridlines,
    CellColor? MajorGridlineColor,
    CellColor? MinorGridlineColor,
    double GridlineThickness,
    ChartAxisTickStyle MajorTickStyle,
    ChartAxisTickStyle MinorTickStyle,
    bool ShowLabels,
    CellColor? LabelTextColor,
    double LabelFontSize,
    double LabelAngle,
    CellColor? LineColor,
    double LineThickness)
{
    public ChartLayoutOptions ToOptions() => UseXAxis
        ? new ChartLayoutOptions(
            XAxisMinimum: Minimum,
            XAxisMaximum: Maximum,
            XAxisMajorUnit: MajorUnit,
            XAxisMinorUnit: MinorUnit,
            XAxisLogScale: LogScale,
            XAxisNumberFormat: NumberFormat,
            ShowXAxisMajorGridlines: ShowMajorGridlines,
            ShowXAxisMinorGridlines: ShowMinorGridlines,
            XAxisMajorGridlineColor: MajorGridlineColor,
            XAxisMinorGridlineColor: MinorGridlineColor,
            XAxisGridlineThickness: GridlineThickness,
            XAxisMajorTickStyle: MajorTickStyle,
            XAxisMinorTickStyle: MinorTickStyle,
            ShowXAxisLabels: ShowLabels,
            XAxisLabelTextColor: LabelTextColor,
            XAxisLabelFontSize: LabelFontSize,
            XAxisLabelAngle: LabelAngle,
            XAxisLineColor: LineColor,
            XAxisLineThickness: LineThickness,
            ClearXAxisBounds: Minimum is null && Maximum is null)
        : new ChartLayoutOptions(
            YAxisMinimum: Minimum,
            YAxisMaximum: Maximum,
            YAxisMajorUnit: MajorUnit,
            YAxisMinorUnit: MinorUnit,
            YAxisLogScale: LogScale,
            YAxisNumberFormat: NumberFormat,
            ShowYAxisMajorGridlines: ShowMajorGridlines,
            ShowYAxisMinorGridlines: ShowMinorGridlines,
            YAxisMajorGridlineColor: MajorGridlineColor,
            YAxisMinorGridlineColor: MinorGridlineColor,
            YAxisGridlineThickness: GridlineThickness,
            YAxisMajorTickStyle: MajorTickStyle,
            YAxisMinorTickStyle: MinorTickStyle,
            ShowYAxisLabels: ShowLabels,
            YAxisLabelTextColor: LabelTextColor,
            YAxisLabelFontSize: LabelFontSize,
            YAxisLabelAngle: LabelAngle,
            YAxisLineColor: LineColor,
            YAxisLineThickness: LineThickness,
            ClearYAxisBounds: Minimum is null && Maximum is null);
}

public sealed class ChartAxisFormatDialog : Window
{
    private readonly bool _useXAxis;
    private readonly TextBox _minimumBox = new();
    private readonly TextBox _maximumBox = new();
    private readonly TextBox _majorUnitBox = new();
    private readonly TextBox _minorUnitBox = new();
    private readonly CheckBox _logBox = new() { Content = "Logarithmic scale" };
    private readonly ComboBox _numberFormatBox = new();
    private readonly CheckBox _majorGridBox = new() { Content = "Major gridlines" };
    private readonly CheckBox _minorGridBox = new() { Content = "Minor gridlines" };
    private readonly TextBox _majorGridColorBox = new();
    private readonly TextBox _minorGridColorBox = new();
    private readonly TextBox _gridlineThicknessBox = new();
    private readonly ComboBox _majorTickBox = new();
    private readonly ComboBox _minorTickBox = new();
    private readonly CheckBox _labelsBox = new() { Content = "Show labels" };
    private readonly TextBox _labelColorBox = new();
    private readonly TextBox _labelFontSizeBox = new();
    private readonly TextBox _labelAngleBox = new();
    private readonly TextBox _lineColorBox = new();
    private readonly TextBox _lineThicknessBox = new();

    public ChartAxisFormatDialogResult Result { get; private set; }

    public ChartAxisFormatDialog(ChartModel chart, bool useXAxis)
    {
        _useXAxis = useXAxis;
        Result = FromChart(chart, useXAxis);
        Title = useXAxis ? "Format X Axis" : "Format Y Axis";
        Width = 430;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
    }

    public static ChartAxisFormatDialogResult FromChart(ChartModel chart, bool useXAxis) => useXAxis
        ? CreateResult(true, chart.XAxisMinimum, chart.XAxisMaximum, chart.XAxisMajorUnit, chart.XAxisMinorUnit,
            chart.XAxisLogScale, chart.XAxisNumberFormat, chart.ShowXAxisMajorGridlines, chart.ShowXAxisMinorGridlines,
            chart.XAxisMajorGridlineColor, chart.XAxisMinorGridlineColor, chart.XAxisGridlineThickness,
            chart.XAxisMajorTickStyle, chart.XAxisMinorTickStyle, chart.ShowXAxisLabels, chart.XAxisLabelTextColor,
            chart.XAxisLabelFontSize, chart.XAxisLabelAngle, chart.XAxisLineColor, chart.XAxisLineThickness)
        : CreateResult(false, chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisMajorUnit, chart.YAxisMinorUnit,
            chart.YAxisLogScale, chart.YAxisNumberFormat, chart.ShowYAxisMajorGridlines, chart.ShowYAxisMinorGridlines,
            chart.YAxisMajorGridlineColor, chart.YAxisMinorGridlineColor, chart.YAxisGridlineThickness,
            chart.YAxisMajorTickStyle, chart.YAxisMinorTickStyle, chart.ShowYAxisLabels, chart.YAxisLabelTextColor,
            chart.YAxisLabelFontSize, chart.YAxisLabelAngle, chart.YAxisLineColor, chart.YAxisLineThickness);

    public static ChartAxisFormatDialogResult CreateResult(
        bool useXAxis,
        double? minimum,
        double? maximum,
        double? majorUnit,
        double? minorUnit,
        bool logScale,
        ChartDataLabelNumberFormat numberFormat,
        bool showMajorGridlines,
        bool showMinorGridlines,
        CellColor? majorGridlineColor,
        CellColor? minorGridlineColor,
        double gridlineThickness,
        ChartAxisTickStyle majorTickStyle,
        ChartAxisTickStyle minorTickStyle,
        bool showLabels,
        CellColor? labelTextColor,
        double labelFontSize,
        double labelAngle,
        CellColor? lineColor,
        double lineThickness) =>
        new(useXAxis, minimum, maximum, majorUnit, minorUnit, logScale, numberFormat, showMajorGridlines,
            showMinorGridlines, majorGridlineColor, minorGridlineColor, gridlineThickness, majorTickStyle,
            minorTickStyle, showLabels, labelTextColor, labelFontSize, labelAngle, lineColor, lineThickness);

    private StackPanel CreateContent()
    {
        var stack = ChartDialogHelpers.DialogStack();
        ChartDialogHelpers.AddText(stack, "Minimum (blank for Auto)", _minimumBox);
        ChartDialogHelpers.AddText(stack, "Maximum (blank for Auto)", _maximumBox);
        ChartDialogHelpers.AddText(stack, "Major unit", _majorUnitBox);
        ChartDialogHelpers.AddText(stack, "Minor unit", _minorUnitBox);
        ChartDialogHelpers.AddCheck(stack, _logBox);
        ChartDialogHelpers.AddCombo(stack, "Number format", _numberFormatBox, Enum.GetValues<ChartDataLabelNumberFormat>());
        ChartDialogHelpers.AddCheck(stack, _majorGridBox);
        ChartDialogHelpers.AddCheck(stack, _minorGridBox);
        ChartDialogHelpers.AddText(stack, "Major gridline color", _majorGridColorBox);
        ChartDialogHelpers.AddText(stack, "Minor gridline color", _minorGridColorBox);
        ChartDialogHelpers.AddText(stack, "Gridline width", _gridlineThicknessBox);
        ChartDialogHelpers.AddCombo(stack, "Major tick marks", _majorTickBox, Enum.GetValues<ChartAxisTickStyle>());
        ChartDialogHelpers.AddCombo(stack, "Minor tick marks", _minorTickBox, Enum.GetValues<ChartAxisTickStyle>());
        ChartDialogHelpers.AddCheck(stack, _labelsBox);
        ChartDialogHelpers.AddText(stack, "Label color", _labelColorBox);
        ChartDialogHelpers.AddText(stack, "Label font size", _labelFontSizeBox);
        ChartDialogHelpers.AddText(stack, "Label angle", _labelAngleBox);
        ChartDialogHelpers.AddText(stack, "Axis line color", _lineColorBox);
        ChartDialogHelpers.AddText(stack, "Axis line width", _lineThicknessBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
    }

    private void Load(ChartAxisFormatDialogResult result)
    {
        _minimumBox.Text = ChartDialogHelpers.FormatNullable(result.Minimum);
        _maximumBox.Text = ChartDialogHelpers.FormatNullable(result.Maximum);
        _majorUnitBox.Text = ChartDialogHelpers.FormatNullable(result.MajorUnit);
        _minorUnitBox.Text = ChartDialogHelpers.FormatNullable(result.MinorUnit);
        _logBox.IsChecked = result.LogScale;
        _numberFormatBox.SelectedItem = result.NumberFormat;
        _majorGridBox.IsChecked = result.ShowMajorGridlines;
        _minorGridBox.IsChecked = result.ShowMinorGridlines;
        _majorGridColorBox.Text = ChartDialogHelpers.FormatColor(result.MajorGridlineColor);
        _minorGridColorBox.Text = ChartDialogHelpers.FormatColor(result.MinorGridlineColor);
        _gridlineThicknessBox.Text = result.GridlineThickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _majorTickBox.SelectedItem = result.MajorTickStyle;
        _minorTickBox.SelectedItem = result.MinorTickStyle;
        _labelsBox.IsChecked = result.ShowLabels;
        _labelColorBox.Text = ChartDialogHelpers.FormatColor(result.LabelTextColor);
        _labelFontSizeBox.Text = result.LabelFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _labelAngleBox.Text = result.LabelAngle.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _lineColorBox.Text = ChartDialogHelpers.FormatColor(result.LineColor);
        _lineThicknessBox.Text = result.LineThickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Accept()
    {
        Result = CreateResult(
            _useXAxis,
            ChartDialogHelpers.ParseNullableDouble(_minimumBox.Text),
            ChartDialogHelpers.ParseNullableDouble(_maximumBox.Text),
            ChartDialogHelpers.ParseNullableDouble(_majorUnitBox.Text),
            ChartDialogHelpers.ParseNullableDouble(_minorUnitBox.Text),
            _logBox.IsChecked == true,
            ChartDialogHelpers.Selected(_numberFormatBox, ChartDataLabelNumberFormat.General),
            _majorGridBox.IsChecked == true,
            _minorGridBox.IsChecked == true,
            ChartDialogHelpers.ParseColor(_majorGridColorBox.Text),
            ChartDialogHelpers.ParseColor(_minorGridColorBox.Text),
            ChartDialogHelpers.ParseDouble(_gridlineThicknessBox.Text, 1),
            ChartDialogHelpers.Selected(_majorTickBox, ChartAxisTickStyle.Outside),
            ChartDialogHelpers.Selected(_minorTickBox, ChartAxisTickStyle.None),
            _labelsBox.IsChecked == true,
            ChartDialogHelpers.ParseColor(_labelColorBox.Text),
            ChartDialogHelpers.ParseDouble(_labelFontSizeBox.Text, 11),
            ChartDialogHelpers.ParseDouble(_labelAngleBox.Text, 0),
            ChartDialogHelpers.ParseColor(_lineColorBox.Text),
            ChartDialogHelpers.ParseDouble(_lineThicknessBox.Text, 1));
        DialogResult = true;
    }
}

public sealed record ChartSeriesFormatDialogResult(
    int SeriesIndex,
    CellColor? FillColor,
    CellColor? StrokeColor,
    double? StrokeThickness,
    ChartLineDashStyle? DashStyle,
    ChartMarkerStyle? MarkerStyle,
    double? MarkerSize)
{
    public ChartLayoutOptions ToOptions(IReadOnlyList<ChartSeriesFormat> currentFormats)
    {
        var formats = currentFormats.ToList();
        var replacement = new ChartSeriesFormat(
            SeriesIndex,
            FillColor,
            StrokeColor,
            StrokeThickness,
            DashStyle,
            MarkerStyle,
            MarkerSize);
        var existingIndex = formats.FindIndex(format => format.SeriesIndex == SeriesIndex);
        if (existingIndex >= 0)
            formats[existingIndex] = replacement;
        else
            formats.Add(replacement);
        return new ChartLayoutOptions(SeriesFormats: formats);
    }
}

public sealed class ChartSeriesFormatDialog : Window
{
    private readonly ComboBox _seriesBox = new();
    private readonly ComboBox _dashBox = new();
    private readonly ComboBox _markerBox = new();
    private readonly TextBox _fillBox = new();
    private readonly TextBox _strokeBox = new();
    private readonly TextBox _strokeThicknessBox = new();
    private readonly TextBox _markerSizeBox = new();

    public ChartSeriesFormatDialogResult Result { get; private set; }

    public ChartSeriesFormatDialog(ChartModel chart, int seriesCount)
    {
        Result = FromChart(chart, seriesCount);
        Title = "Format Data Series";
        Width = 380;
        Height = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent(seriesCount);
        Load(Result);
    }

    public static ChartSeriesFormatDialogResult FromChart(ChartModel chart, int seriesCount)
    {
        var seriesIndex = Math.Clamp(chart.SeriesFormats.FirstOrDefault()?.SeriesIndex ?? 0, 0, Math.Max(0, seriesCount - 1));
        var format = chart.SeriesFormats.FirstOrDefault(item => item.SeriesIndex == seriesIndex) ?? new ChartSeriesFormat(seriesIndex);
        return CreateResult(seriesIndex, format.FillColor, format.StrokeColor, format.StrokeThickness, format.DashStyle, format.MarkerStyle, format.MarkerSize);
    }

    public static ChartSeriesFormatDialogResult CreateResult(
        int seriesIndex,
        CellColor? fillColor,
        CellColor? strokeColor,
        double? strokeThickness,
        ChartLineDashStyle? dashStyle,
        ChartMarkerStyle? markerStyle,
        double? markerSize) =>
        new(Math.Max(0, seriesIndex), fillColor, strokeColor, strokeThickness, dashStyle, markerStyle, markerSize);

    private StackPanel CreateContent(int seriesCount)
    {
        var stack = ChartDialogHelpers.DialogStack();
        ChartDialogHelpers.AddCombo(stack, "Series", _seriesBox, Enumerable.Range(0, Math.Max(1, seriesCount)).Select(index => $"Series {index + 1}").ToArray());
        ChartDialogHelpers.AddText(stack, "Fill color", _fillBox);
        ChartDialogHelpers.AddText(stack, "Line color", _strokeBox);
        ChartDialogHelpers.AddText(stack, "Line width", _strokeThicknessBox);
        ChartDialogHelpers.AddCombo(stack, "Dash style", _dashBox, Enum.GetValues<ChartLineDashStyle>().Cast<object>().Prepend("(none)").ToArray());
        ChartDialogHelpers.AddCombo(stack, "Marker", _markerBox, Enum.GetValues<ChartMarkerStyle>().Cast<object>().Prepend("(none)").ToArray());
        ChartDialogHelpers.AddText(stack, "Marker size", _markerSizeBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
    }

    private void Load(ChartSeriesFormatDialogResult result)
    {
        _seriesBox.SelectedIndex = Math.Min(result.SeriesIndex, Math.Max(0, _seriesBox.Items.Count - 1));
        _fillBox.Text = ChartDialogHelpers.FormatColor(result.FillColor);
        _strokeBox.Text = ChartDialogHelpers.FormatColor(result.StrokeColor);
        _strokeThicknessBox.Text = ChartDialogHelpers.FormatNullable(result.StrokeThickness);
        _dashBox.SelectedItem = result.DashStyle is null ? "(none)" : result.DashStyle.Value;
        _markerBox.SelectedItem = result.MarkerStyle is null ? "(none)" : result.MarkerStyle.Value;
        _markerSizeBox.Text = ChartDialogHelpers.FormatNullable(result.MarkerSize);
    }

    private void Accept()
    {
        Result = CreateResult(
            _seriesBox.SelectedIndex < 0 ? 0 : _seriesBox.SelectedIndex,
            ChartDialogHelpers.ParseColor(_fillBox.Text),
            ChartDialogHelpers.ParseColor(_strokeBox.Text),
            ChartDialogHelpers.ParseNullableDouble(_strokeThicknessBox.Text),
            _dashBox.SelectedItem is ChartLineDashStyle dash ? dash : null,
            _markerBox.SelectedItem is ChartMarkerStyle marker ? marker : null,
            ChartDialogHelpers.ParseNullableDouble(_markerSizeBox.Text));
        DialogResult = true;
    }
}

internal static class ChartDialogHelpers
{
public static StackPanel DialogStack() => new() { Margin = new Thickness(16) };

public static void AddCheck(Panel stack, CheckBox checkBox)
{
    checkBox.Margin = new Thickness(0, 0, 0, 6);
    stack.Children.Add(checkBox);
}

public static void AddCombo<T>(Panel stack, string label, ComboBox comboBox, IEnumerable<T> items)
{
    stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 4) });
    comboBox.ItemsSource = items;
    comboBox.Margin = new Thickness(0, 0, 0, 8);
    stack.Children.Add(comboBox);
}

public static void AddText(Panel stack, string label, TextBox textBox)
{
    stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 4) });
    textBox.Margin = new Thickness(0, 0, 0, 8);
    stack.Children.Add(textBox);
}

public static T Selected<T>(ComboBox comboBox, T fallback) =>
    comboBox.SelectedItem is T value ? value : fallback;

public static CellColor? ParseColor(string text) =>
    ColorInputParser.TryParseOptionalHexColor(text, out var color) ? color : null;

public static string FormatColor(CellColor? color) =>
    color is null ? "none" : ColorInputParser.FormatHexColor(color.Value);

public static double ParseDouble(string text, double fallback) =>
    double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
        ? value
        : fallback;

public static double? ParseNullableDouble(string text) =>
    string.IsNullOrWhiteSpace(text) || text.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)
        ? null
        : ChartDialogHelpers.ParseDouble(text, 0);

public static string FormatNullable(double? value) =>
    value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
}
