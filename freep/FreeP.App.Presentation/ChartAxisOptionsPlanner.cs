using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartAxisKindOption(ChartAxisKind Value, string Label);
public sealed record ChartTickMarkOption(ChartTickMark? Value, string Label);
public sealed record ChartTickLabelPositionOption(ChartTickLabelPosition? Value, string Label);
public sealed record ChartAxisCrossingOption(ChartAxisCrossing? Value, string Label);
public sealed record ChartCrossBetweenOption(ChartCrossBetween? Value, string Label);
public sealed record ChartLabelAlignmentOption(ChartLabelAlignment? Value, string Label);
public sealed record ChartAxisBooleanOption(bool? Value, string Label);
public sealed record ChartAxisDisplayUnitOption(ChartAxisDisplayUnit Value, string Label);

public sealed record ChartAxisOptionsSurfacePlan(
    string CommandId,
    string Title,
    string AxisLabel,
    string CategoryAxisLabel,
    string ValueAxisLabel,
    string SecondaryValueAxisLabel,
    string ShowAxisLabel,
    string AxisTitleLabel,
    string MinimumLabel,
    string MaximumLabel,
    string MajorUnitLabel,
    string MinorUnitLabel,
    string NumberFormatLabel,
    string DisplayUnitLabel,
    string MajorGridlinesLabel,
    string MinorGridlinesLabel,
    string AxisTitleFontFamilyLabel,
    string AxisTitleFontSizeLabel,
    string AxisTitleBoldLabel,
    string AxisTitleItalicLabel,
    string AxisTitleColorLabel,
    string MajorTickMarkLabel,
    string MinorTickMarkLabel,
    string TickLabelPositionLabel,
    string CrossingLabel,
    string CrossesAtLabel,
    string CrossBetweenLabel,
    string LabelAlignmentLabel,
    string LabelOffsetLabel,
    string MultiLevelLabelsLabel,
    string AutoCrossingLabel,
    string ReverseOrderLabel,
    string AutoHint,
    string OkLabel,
    string CancelLabel);

/// <summary>
/// Working-copy planner for the chart axis scale and display controls already supported by the
/// chart model, renderer, and OOXML reader/writer.
/// </summary>
public sealed class ChartAxisOptionsPlanner
{
    public const string CommandId = "freep.chart.axis-options";
    public const string DialogTitle = "Chart Axis Options";
    public const string AxisLabel = "Axis";
    public const string CategoryAxisLabel = "Category axis";
    public const string ValueAxisLabel = "Value axis";
    public const string SecondaryValueAxisLabel = "Secondary value axis";
    public const string ShowAxisLabel = "Show axis";
    public const string AxisTitleLabel = "Axis title";
    public const string MinimumLabel = "Minimum";
    public const string MaximumLabel = "Maximum";
    public const string MajorUnitLabel = "Major unit";
    public const string MinorUnitLabel = "Minor unit";
    public const string NumberFormatLabel = "Number format";
    public const string DisplayUnitLabel = "Display units";
    public const string MajorGridlinesLabel = "Major gridlines";
    public const string MinorGridlinesLabel = "Minor gridlines";
    public const string AxisTitleFontFamilyLabel = "Axis title font family";
    public const string AxisTitleFontSizeLabel = "Axis title size (pt)";
    public const string AxisTitleBoldLabel = "Axis title bold";
    public const string AxisTitleItalicLabel = "Axis title italic";
    public const string AxisTitleColorLabel = "Axis title color (#RRGGBB)";
    public const string MajorTickMarkLabel = "Major tick marks";
    public const string MinorTickMarkLabel = "Minor tick marks";
    public const string TickLabelPositionLabel = "Tick labels";
    public const string CrossingLabel = "Axis crosses";
    public const string CrossesAtLabel = "Crosses at";
    public const string CrossBetweenLabel = "Cross between";
    public const string LabelAlignmentLabel = "Label alignment";
    public const string LabelOffsetLabel = "Label offset (%)";
    public const string MultiLevelLabelsLabel = "Multi-level labels";
    public const string AutoCrossingLabel = "Automatic crossing";
    public const string ReverseOrderLabel = "Reverse order";
    public const string AutoHint = "Blank values use automatic chart scaling.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 480;
    public const double DefaultDialogHeight = 490;

    public static IReadOnlyList<ChartAxisKindOption> AxisOptions { get; } =
    [
        new(ChartAxisKind.Category, CategoryAxisLabel),
        new(ChartAxisKind.Value, ValueAxisLabel),
        new(ChartAxisKind.SecondaryValue, SecondaryValueAxisLabel),
    ];

    public static IReadOnlyList<ChartTickMarkOption> TickMarkOptions { get; } =
    [
        new(null, "Automatic"),
        new(ChartTickMark.None, "None"),
        new(ChartTickMark.In, "Inside"),
        new(ChartTickMark.Out, "Outside"),
        new(ChartTickMark.Cross, "Cross"),
    ];

    public static IReadOnlyList<ChartTickLabelPositionOption> TickLabelPositionOptions { get; } =
    [
        new(null, "Automatic"),
        new(ChartTickLabelPosition.None, "None"),
        new(ChartTickLabelPosition.Low, "Low"),
        new(ChartTickLabelPosition.High, "High"),
        new(ChartTickLabelPosition.NextTo, "Next to axis"),
    ];

    public static IReadOnlyList<ChartAxisCrossingOption> CrossingOptions { get; } =
    [
        new(null, "Automatic"),
        new(ChartAxisCrossing.AutoZero, "Automatic zero"),
        new(ChartAxisCrossing.Min, "Minimum"),
        new(ChartAxisCrossing.Max, "Maximum"),
    ];

    public static IReadOnlyList<ChartCrossBetweenOption> CrossBetweenOptions { get; } =
    [
        new(null, "Automatic"),
        new(ChartCrossBetween.Between, "Between categories"),
        new(ChartCrossBetween.MidCat, "On category"),
    ];

    public static IReadOnlyList<ChartLabelAlignmentOption> LabelAlignmentOptions { get; } =
    [
        new(null, "Automatic"),
        new(ChartLabelAlignment.Left, "Left"),
        new(ChartLabelAlignment.Center, "Center"),
        new(ChartLabelAlignment.Right, "Right"),
    ];

    public static IReadOnlyList<ChartAxisBooleanOption> MultiLevelLabelsOptions { get; } =
    [
        new(null, "Automatic"),
        new(false, "Show multi-level labels"),
        new(true, "Hide multi-level labels"),
    ];

    public static IReadOnlyList<ChartAxisBooleanOption> AutoCrossingOptions { get; } =
    [
        new(null, "Automatic"),
        new(true, "Enable automatic crossing"),
        new(false, "Use explicit crossing"),
    ];

    public static IReadOnlyList<ChartAxisDisplayUnitOption> DisplayUnitOptions { get; } =
    [
        new(ChartAxisDisplayUnit.None, "None"),
        new(ChartAxisDisplayUnit.Hundreds, "Hundreds"),
        new(ChartAxisDisplayUnit.Thousands, "Thousands"),
        new(ChartAxisDisplayUnit.TenThousands, "Ten thousands"),
        new(ChartAxisDisplayUnit.HundredThousands, "Hundred thousands"),
        new(ChartAxisDisplayUnit.Millions, "Millions"),
        new(ChartAxisDisplayUnit.TenMillions, "Ten millions"),
        new(ChartAxisDisplayUnit.HundredMillions, "Hundred millions"),
        new(ChartAxisDisplayUnit.Billions, "Billions"),
        new(ChartAxisDisplayUnit.Trillions, "Trillions"),
        new(ChartAxisDisplayUnit.Custom, "Custom"),
        new(ChartAxisDisplayUnit.Unsupported, "Preserve unknown source unit"),
    ];

    private readonly ChartShape _chart;
    private ChartAxisKind _axisKind;
    private string _title = string.Empty;
    private bool _showAxis;
    private double? _minimum;
    private double? _maximum;
    private double? _majorUnit;
    private double? _minorUnit;
    private string _numberFormat = string.Empty;
    private ChartAxisDisplayUnit _displayUnit;
    private string? _rawDisplayUnitToken;
    private double? _customDisplayUnit;
    private bool _majorGridlines;
    private bool _minorGridlines;
    private ChartTickMark? _majorTickMark;
    private string? _rawMajorTickMarkToken;
    private ChartTickMark? _minorTickMark;
    private string? _rawMinorTickMarkToken;
    private ChartTickLabelPosition? _tickLabelPosition;
    private string? _rawTickLabelPositionToken;
    private ChartAxisCrossing? _crosses;
    private string? _rawCrossesToken;
    private double? _crossesAt;
    private ChartCrossBetween? _crossBetween;
    private string? _rawCrossBetweenToken;
    private ChartLabelAlignment? _labelAlignment;
    private string? _rawLabelAlignmentToken;
    private int? _labelOffsetPercent;
    private bool? _noMultiLevelLabels;
    private bool? _autoCrossing;
    private bool _reverseOrder;
    private string? _titleFontFamily;
    private double? _titleFontSizePt;
    private bool? _titleBold;
    private bool? _titleItalic;
    private ThemeAwareColor? _titleColor;

    private ChartAxisOptionsPlanner(ChartShape chart)
    {
        _chart = chart;
        SetAxis(ChartAxisKind.Value);
    }

    public static ChartAxisOptionsSurfacePlan BuildSurfacePlan() =>
        new(
            CommandId,
            DialogTitle,
            AxisLabel,
            CategoryAxisLabel,
            ValueAxisLabel,
            SecondaryValueAxisLabel,
            ShowAxisLabel,
            AxisTitleLabel,
            MinimumLabel,
            MaximumLabel,
            MajorUnitLabel,
            MinorUnitLabel,
            NumberFormatLabel,
            DisplayUnitLabel,
            MajorGridlinesLabel,
            MinorGridlinesLabel,
            AxisTitleFontFamilyLabel,
            AxisTitleFontSizeLabel,
            AxisTitleBoldLabel,
            AxisTitleItalicLabel,
            AxisTitleColorLabel,
            MajorTickMarkLabel,
            MinorTickMarkLabel,
            TickLabelPositionLabel,
            CrossingLabel,
            CrossesAtLabel,
            CrossBetweenLabel,
            LabelAlignmentLabel,
            LabelOffsetLabel,
            MultiLevelLabelsLabel,
            AutoCrossingLabel,
            ReverseOrderLabel,
            AutoHint,
            OkLabel,
            CancelLabel);

    public static ChartAxisOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartAxisOptionsPlanner(chart);
    }

    public ChartAxisKind Axis => _axisKind;
    public string Title => _title;
    public bool ShowAxis => _showAxis;
    public double? Minimum => _minimum;
    public double? Maximum => _maximum;
    public double? MajorUnit => _majorUnit;
    public double? MinorUnit => _minorUnit;
    public string NumberFormatCode => _numberFormat;
    public ChartAxisDisplayUnit DisplayUnit => _displayUnit;
    public string? RawDisplayUnitToken => _rawDisplayUnitToken;
    public double? CustomDisplayUnit => _customDisplayUnit;
    public bool MajorGridlines => _majorGridlines;
    public bool MinorGridlines => _minorGridlines;
    public ChartTickMark? MajorTickMark => _majorTickMark;
    public ChartTickMark? MinorTickMark => _minorTickMark;
    public ChartTickLabelPosition? TickLabelPosition => _tickLabelPosition;
    public ChartAxisCrossing? Crosses => _crosses;
    public double? CrossesAt => _crossesAt;
    public ChartCrossBetween? CrossBetween => _crossBetween;
    public ChartLabelAlignment? LabelAlignment => _labelAlignment;
    public int? LabelOffsetPercent => _labelOffsetPercent;
    public bool? NoMultiLevelLabels => _noMultiLevelLabels;
    public bool? AutoCrossing => _autoCrossing;
    public bool ReverseOrder => _reverseOrder;
    public string? TitleFontFamily => _titleFontFamily;
    public double? TitleFontSizePt => _titleFontSizePt;
    public bool? TitleBold => _titleBold;
    public bool? TitleItalic => _titleItalic;
    public string TitleColorText => _titleColor is null ? string.Empty : _titleColor.Resolved.ToString();

    public void SetAxis(ChartAxisKind axisKind)
    {
        _axisKind = axisKind;
        var axis = ResolveAxis();
        _title = axis.Title ?? string.Empty;
        _showAxis = !axis.Delete;
        _minimum = axis.Min;
        _maximum = axis.Max;
        _majorUnit = axis.MajorUnit;
        _minorUnit = axis.MinorUnit;
        _numberFormat = axis.NumberFormatCode ?? string.Empty;
        _displayUnit = axis.DisplayUnit;
        _rawDisplayUnitToken = axis.RawDisplayUnitToken;
        _customDisplayUnit = axis.CustomDisplayUnit;
        _majorGridlines = axis.HasMajorGridlines;
        _minorGridlines = axis.HasMinorGridlines;
        _majorTickMark = axis.MajorTickMark;
        _rawMajorTickMarkToken = axis.RawMajorTickMarkToken;
        _minorTickMark = axis.MinorTickMark;
        _rawMinorTickMarkToken = axis.RawMinorTickMarkToken;
        _tickLabelPosition = axis.TickLabelPosition;
        _rawTickLabelPositionToken = axis.RawTickLabelPositionToken;
        _crosses = axis.Crosses;
        _rawCrossesToken = axis.RawCrossesToken;
        _crossesAt = axis.CrossesAt;
        _crossBetween = axis.CrossBetween;
        _rawCrossBetweenToken = axis.RawCrossBetweenToken;
        _labelAlignment = axis.LabelAlignment;
        _rawLabelAlignmentToken = axis.RawLabelAlignmentToken;
        _labelOffsetPercent = axis.LabelOffsetPercent;
        _noMultiLevelLabels = axis.NoMultiLevelLabels;
        _autoCrossing = axis.AutoCrossing;
        _reverseOrder = axis.ReverseOrder;
        _titleFontFamily = axis.TitleStyle?.FontFamily;
        _titleFontSizePt = axis.TitleStyle?.FontSizePt;
        _titleBold = axis.TitleStyle?.Bold;
        _titleItalic = axis.TitleStyle?.Italic;
        _titleColor = axis.TitleStyle?.Color;
    }

    public void SetTitle(string? title) => _title = title ?? string.Empty;
    public void SetShowAxis(bool show) => _showAxis = show;
    public void SetMinimum(double? minimum) => _minimum = minimum;
    public void SetMaximum(double? maximum) => _maximum = maximum;
    public void SetMajorUnit(double? majorUnit) => _majorUnit = majorUnit;
    public void SetMinorUnit(double? minorUnit) => _minorUnit = minorUnit;
    public void SetNumberFormatCode(string? formatCode) => _numberFormat = formatCode ?? string.Empty;
    public void SetDisplayUnit(ChartAxisDisplayUnit value) => _displayUnit = value;
    public void SetCustomDisplayUnit(double? value) => _customDisplayUnit =
        value is null ? null : Math.Clamp(value.Value, 0.000001, 1_000_000_000_000d);
    public void SetMajorGridlines(bool show) => _majorGridlines = show;
    public void SetMinorGridlines(bool show) => _minorGridlines = show;
    public void SetMajorTickMark(ChartTickMark? value)
    {
        _majorTickMark = value;
        _rawMajorTickMarkToken = null;
    }

    public void SetMinorTickMark(ChartTickMark? value)
    {
        _minorTickMark = value;
        _rawMinorTickMarkToken = null;
    }

    public void SetTickLabelPosition(ChartTickLabelPosition? value)
    {
        _tickLabelPosition = value;
        _rawTickLabelPositionToken = null;
    }

    public void SetCrosses(ChartAxisCrossing? value)
    {
        _crosses = value;
        _rawCrossesToken = null;
    }

    public void SetCrossesAt(double? value)
    {
        _crossesAt = value;
        _rawCrossesToken = null;
    }

    public void SetCrossBetween(ChartCrossBetween? value)
    {
        _crossBetween = value;
        _rawCrossBetweenToken = null;
    }

    public void SetLabelAlignment(ChartLabelAlignment? value)
    {
        _labelAlignment = value;
        _rawLabelAlignmentToken = null;
    }
    public void SetLabelOffsetPercent(int? value) => _labelOffsetPercent = value;
    public void SetNoMultiLevelLabels(bool? value) => _noMultiLevelLabels = value;
    public void SetAutoCrossing(bool? value) => _autoCrossing = value;
    public void SetReverseOrder(bool value) => _reverseOrder = value;
    public void SetTitleFontFamily(string? value) => _titleFontFamily =
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public void SetTitleFontSizePt(double? value) => _titleFontSizePt =
        value is null ? null : Math.Clamp(value.Value, 1, 400);
    public void SetTitleBold(bool? value) => _titleBold = value;
    public void SetTitleItalic(bool? value) => _titleItalic = value;
    public void SetTitleColor(string? value) => _titleColor = ChartPointOptionsPlanner.ParseColor(value, AxisTitleColorLabel);

    public ChartAxisOptions BuildCommitPlan() => new(
        _axisKind,
        string.IsNullOrWhiteSpace(_title) ? null : _title.Trim(),
        _minimum,
        _maximum,
        _majorUnit,
        _minorUnit,
        string.IsNullOrWhiteSpace(_numberFormat) ? null : _numberFormat.Trim(),
        _majorGridlines,
        _majorTickMark,
        _minorTickMark,
        _tickLabelPosition,
        _crossesAt is null ? _crosses : null,
        _crossesAt,
        _showAxis,
        _crossBetween,
        _labelAlignment,
        _labelOffsetPercent,
        _noMultiLevelLabels,
        _autoCrossing,
        _reverseOrder,
        _minorGridlines,
        BuildTitleStyle(),
        _displayUnit,
        _displayUnit == ChartAxisDisplayUnit.Unsupported ? _rawDisplayUnitToken : null,
        _displayUnit == ChartAxisDisplayUnit.Custom ? _customDisplayUnit : null,
        _rawMajorTickMarkToken,
        _rawMinorTickMarkToken,
        _rawTickLabelPositionToken,
        _rawCrossesToken,
        _rawCrossBetweenToken,
        _rawLabelAlignmentToken);

    private ChartTextStyle? BuildTitleStyle() =>
        _titleFontFamily is null && _titleFontSizePt is null && _titleBold is null &&
        _titleItalic is null && _titleColor is null
            ? null
            : new ChartTextStyle
            {
                FontFamily = _titleFontFamily,
                FontSizePt = _titleFontSizePt,
                Bold = _titleBold,
                Italic = _titleItalic,
                Color = _titleColor,
            };

    private ChartAxis ResolveAxis() =>
        _axisKind switch
        {
            ChartAxisKind.Category => _chart.CategoryAxis,
            ChartAxisKind.Value => _chart.ValueAxis,
            ChartAxisKind.SecondaryValue => _chart.SecondaryValueAxis ?? new ChartAxis(),
            _ => throw new ArgumentOutOfRangeException(),
        };
}
