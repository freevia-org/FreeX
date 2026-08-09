using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartAreaOptionsDialogTestSettings(
    ChartAreaFormattingTarget Target,
    string? Fill,
    string? Outline,
    double? Width,
    bool NoFill = false,
    bool NoOutline = false,
    double? FillTransparency = null);

public sealed record ChartLayoutOptionsDialogTestSettings(
    ChartLayoutTarget Target,
    string? LayoutTarget,
    ChartManualLayoutMode XMode,
    ChartManualLayoutMode YMode,
    ChartManualLayoutMode WidthMode,
    ChartManualLayoutMode HeightMode,
    double? X,
    double? Y,
    double? Width,
    double? Height);

public sealed record ChartBubbleOptionsDialogTestSettings(
    int BubbleScalePercent,
    BubbleSizeRepresentation SizeRepresents,
    bool ShowNegativeBubbles);

public sealed record ChartPieOptionsDialogTestSettings(
    int? FirstSliceAngleDegrees,
    int DoughnutHolePercent);

public sealed record ChartOfPieOptionsDialogTestSettings(
    OfPieType Type,
    OfPieSplitType SplitType,
    double? SplitPosition,
    int SecondPieSizePercent,
    string CustomPointIndices,
    int? GapWidthPercent = null,
    bool SeriesLines = false);

public sealed record ChartPlotStyleOptionsDialogTestSettings(
    ScatterStyle ScatterStyle,
    RadarStyle RadarStyle);

public sealed record ChartProtectionOptionsDialogTestSettings(
    bool? ChartObject,
    bool? Data,
    bool? Formatting,
    bool? Selection);

public sealed record Chart3DViewOptionsDialogTestSettings(
    int? RotationX,
    int? RotationY,
    int? Perspective,
    int? HeightPercent,
    int? DepthPercent,
    bool? RightAngleAxes,
    bool? Wireframe,
    int? BarGapDepthPercent = null);

public sealed record ChartTextOptionsDialogTestSettings(
    string? FontFamily,
    double? FontSizePt,
    bool? Bold,
    bool? Italic,
    string? Color);

public sealed record ChartDataTableOptionsDialogTestSettings(
    bool ShowDataTable,
    bool ShowHorizontalBorder,
    bool ShowVerticalBorder,
    bool ShowOutlineBorder,
    bool ShowLegendKeys,
    string? BackgroundColor = null,
    string? BorderColor = null,
    double? BorderWidthPt = null,
    string? TextColor = null,
    double? FontSizePt = null,
    string? FontFamily = null,
    bool? Bold = null,
    bool? Italic = null);

public sealed record ChartAxisOptionsDialogTestSettings(
    ChartAxisKind Axis,
    string Title,
    double? Minimum,
    double? Maximum,
    double? MajorUnit,
    double? MinorUnit,
    string NumberFormatCode,
    bool MajorGridlines,
    ChartTickMark? MajorTickMark = null,
    ChartTickMark? MinorTickMark = null,
    ChartTickLabelPosition? TickLabelPosition = null,
    ChartAxisCrossing? Crosses = null,
    double? CrossesAt = null,
    bool ShowAxis = true,
    ChartCrossBetween? CrossBetween = null,
    ChartLabelAlignment? LabelAlignment = null,
    int? LabelOffsetPercent = null,
    bool? NoMultiLevelLabels = null,
    bool? AutoCrossing = null,
    bool ReverseOrder = false,
    bool MinorGridlines = false);

public sealed record ChartPointOptionsDialogTestSettings(
    int SeriesIndex,
    int PointIndex,
    string? FillColor,
    string? StrokeColor,
    double? StrokeWidthPt,
    ChartMarkerSymbol? MarkerSymbol,
    double? MarkerSizePt,
    bool UsePointDataLabels = false,
    bool ShowValueLabels = false,
    bool ShowPercentLabels = false,
    bool ShowCategoryLabels = false,
    bool ShowSeriesLabels = false,
    bool ShowLegendKeys = false,
    DataLabelPosition LabelPosition = DataLabelPosition.OutsideEnd,
    string? LabelNumberFormat = null,
    string? LabelSeparator = null,
    string? LabelFontFamily = null,
    double? LabelFontSizePt = null,
    bool? LabelBold = null,
    bool? LabelItalic = null,
    string? LabelColor = null,
    bool ShowBubbleSize = false,
    int? ExplosionPercent = null,
    bool? ShowLeaderLines = null);

public sealed record ChartSeriesOptionsDialogTestSettings(
    int SeriesIndex,
    bool SmoothLine,
    bool OnSecondaryAxis,
    double? LineWidthPt,
    ChartMarkerSymbol MarkerSymbol,
    double? MarkerSizePt,
    string? FillColor = null,
    string? LineColor = null,
    OutlineDash LineDash = OutlineDash.Solid,
    bool NoLine = false,
    bool UseSeriesDataLabels = false,
    bool ShowValueLabels = false,
    bool ShowPercentLabels = false,
    bool ShowCategoryLabels = false,
    bool ShowSeriesLabels = false,
    bool ShowLegendKeys = false,
    DataLabelPosition LabelPosition = DataLabelPosition.OutsideEnd,
    string? LabelNumberFormat = null,
    string? LabelSeparator = null,
    string? LabelFontFamily = null,
    double? LabelFontSizePt = null,
    bool? LabelBold = null,
    bool? LabelItalic = null,
    string? LabelColor = null,
    bool ShowBubbleSize = false,
    bool? ShowLeaderLines = null,
    bool ErrorBars = false,
    bool Trendline = false,
    ChartTrendlineType TrendlineType = ChartTrendlineType.Linear,
    int? TrendlineOrder = null,
    int? TrendlinePeriod = null,
    double? TrendlineForward = null,
    double? TrendlineBackward = null,
    bool TrendlineEquation = false,
    bool TrendlineRSquared = false,
    ChartType? OverrideChartType = null,
    bool? InvertIfNegative = null);

public sealed record ChartDisplayOptionsDialogTestSettings
{
    public string? Title { get; init; }
    public bool? TitleOverlay { get; init; }
    public ChartExTitlePosition? TitlePosition { get; init; }
    public ChartExTitleAlignment? TitleAlignment { get; init; }
    public bool? PlotVisibleOnly { get; init; }
    public bool? RoundedCorners { get; init; }
    public int? StyleId { get; init; }
    public LegendPosition? Legend { get; init; }
    public bool? ShowValueLabels { get; init; }
    public bool? ShowPercentLabels { get; init; }
    public bool? ShowCategoryLabels { get; init; }
    public bool? ShowSeriesLabels { get; init; }
    public bool? ShowLegendKeys { get; init; }
    public bool? ShowBubbleSize { get; init; }
    public bool? ShowLeaderLines { get; init; }
    public string? LabelNumberFormat { get; init; }
    public string? LabelSeparator { get; init; }
    public string? LabelFontFamily { get; init; }
    public double? LabelFontSizePt { get; init; }
    public bool? LabelBold { get; init; }
    public bool? LabelItalic { get; init; }
    public string? LabelColor { get; init; }
    public DataLabelPosition? LabelPosition { get; init; }
    public bool? CategoryGridlines { get; init; }
    public bool? ValueGridlines { get; init; }
    public int? BarGapWidthPercent { get; init; }
    public int? BarOverlapPercent { get; init; }
    public ChartDisplayBlanksAs? DisplayBlanksAs { get; init; }
    public bool? ShowDataLabelsOverMaximum { get; init; }
    public bool? VaryColors { get; init; }
    public bool? LegendOverlay { get; init; }
    public bool? HighLowLines { get; init; }
    public bool? WaterfallConnectorLines { get; init; }
    public bool? DropLines { get; init; }
    public bool? UpDownBars { get; init; }
    public bool? SeriesLines { get; init; }
}

public static class ChartOptionsDialogTestPlanCatalog
{
    public static ChartOptionsDialogValues BuildTestValues(
        this ChartAreaOptionsDialogSession session,
        ChartAreaOptionsDialogTestSettings settings,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(culture);
        return Overlay(session.BuildDialogPlan(culture), Values(
            Choice(ChartOptionsDialogFieldId.AreaTarget, FindIndex(
                ChartAreaOptionsPlanner.TargetOptions, settings.Target, option => option.Value)),
            Text(ChartOptionsDialogFieldId.FillColor, settings.Fill),
            Text(ChartOptionsDialogFieldId.FillTransparency, Format(settings.FillTransparency, culture)),
            Checked(ChartOptionsDialogFieldId.NoFill, settings.NoFill),
            Text(ChartOptionsDialogFieldId.OutlineColor, settings.Outline),
            Text(ChartOptionsDialogFieldId.OutlineWidth, Format(settings.Width, culture)),
            Checked(ChartOptionsDialogFieldId.NoOutline, settings.NoOutline)));
    }

    public static ChartOptionsDialogValues BuildTestValues(
        this ChartLayoutOptionsDialogSession session,
        ChartLayoutOptionsDialogTestSettings settings,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(culture);
        var layoutTargets = ChartLayoutOptionsPlanner.LayoutTargetOptionsFor(settings.LayoutTarget);
        return Overlay(session.BuildDialogPlan(culture), Values(
            Choice(ChartOptionsDialogFieldId.LayoutTargetObject, FindIndex(
                ChartLayoutOptionsPlanner.TargetOptions, settings.Target, option => option.Value)),
            Choice(ChartOptionsDialogFieldId.LayoutTarget, FindIndex(
                layoutTargets, settings.LayoutTarget, option => option.Value, StringComparer.OrdinalIgnoreCase)),
            Choice(ChartOptionsDialogFieldId.XMode, ModeIndex(settings.XMode)),
            Choice(ChartOptionsDialogFieldId.YMode, ModeIndex(settings.YMode)),
            Choice(ChartOptionsDialogFieldId.WidthMode, ModeIndex(settings.WidthMode)),
            Choice(ChartOptionsDialogFieldId.HeightMode, ModeIndex(settings.HeightMode)),
            Text(ChartOptionsDialogFieldId.X, Format(settings.X, culture)),
            Text(ChartOptionsDialogFieldId.Y, Format(settings.Y, culture)),
            Text(ChartOptionsDialogFieldId.Width, Format(settings.Width, culture)),
            Text(ChartOptionsDialogFieldId.Height, Format(settings.Height, culture))));
    }

    public static ChartOptionsDialogValues BuildTestValues(
        this ChartBubbleOptionsDialogSession session,
        ChartBubbleOptionsDialogTestSettings settings) => Overlay(session.BuildDialogPlan(), Values(
            Text(ChartOptionsDialogFieldId.BubbleScale, session.Format(settings.BubbleScalePercent)),
            Choice(ChartOptionsDialogFieldId.BubbleSizeRepresents, session.FindSizeRepresentsIndex(settings.SizeRepresents)),
            Checked(ChartOptionsDialogFieldId.ShowNegativeBubbles, settings.ShowNegativeBubbles)));

    public static ChartOptionsDialogValues BuildTestValues(
        this ChartPieOptionsDialogSession session,
        ChartPieOptionsDialogTestSettings settings,
        CultureInfo culture) => Overlay(session.BuildDialogPlan(culture), Values(
            Text(ChartOptionsDialogFieldId.FirstSliceAngle, Format(settings.FirstSliceAngleDegrees ?? 0, culture)),
            Text(ChartOptionsDialogFieldId.DoughnutHole, Format(settings.DoughnutHolePercent, culture))));

    public static ChartOptionsDialogValues BuildTestValues(
        this ChartPieOptionsDialogSession session,
        ChartOfPieOptionsDialogTestSettings settings,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(culture);
        if (!session.State.IsOfPie)
            throw new InvalidOperationException("The selected chart is not an OfPie chart.");
        return Overlay(session.BuildDialogPlan(culture), Values(
            Choice(ChartOptionsDialogFieldId.OfPieType, settings.Type == OfPieType.Bar ? 1 : 0),
            Choice(ChartOptionsDialogFieldId.OfPieSplitType, (int)settings.SplitType),
            Text(ChartOptionsDialogFieldId.OfPieSplitPosition, Format(settings.SplitPosition ?? 0, culture)),
            Text(ChartOptionsDialogFieldId.OfPieSecondPieSize, Format(settings.SecondPieSizePercent, culture)),
            Text(ChartOptionsDialogFieldId.OfPieCustomPointIndices, settings.CustomPointIndices),
            Text(ChartOptionsDialogFieldId.OfPieGapWidth, Format(settings.GapWidthPercent, culture)),
            Checked(ChartOptionsDialogFieldId.OfPieSeriesLines, settings.SeriesLines)));
    }

    public static ChartOptionsDialogValues BuildTestValues(
        this ChartPlotStyleOptionsDialogSession session,
        ChartPlotStyleOptionsDialogTestSettings settings) => Overlay(session.BuildDialogPlan(), Values(
            Choice(ChartOptionsDialogFieldId.ScatterStyle, session.FindScatterIndex(settings.ScatterStyle)),
            Choice(ChartOptionsDialogFieldId.RadarStyle, session.FindRadarIndex(settings.RadarStyle))));

    public static ChartOptionsDialogValues BuildTestValues(
        this ChartProtectionOptionsDialogSession session,
        ChartProtectionOptionsDialogTestSettings settings) => Overlay(session.BuildDialogPlan(), Values(
            Choice(ChartOptionsDialogFieldId.ProtectedChartObject, session.FindBooleanIndex(settings.ChartObject)),
            Choice(ChartOptionsDialogFieldId.ProtectedData, session.FindBooleanIndex(settings.Data)),
            Choice(ChartOptionsDialogFieldId.ProtectedFormatting, session.FindBooleanIndex(settings.Formatting)),
            Choice(ChartOptionsDialogFieldId.ProtectedSelection, session.FindBooleanIndex(settings.Selection))));

    public static ChartOptionsDialogValues BuildTestValues(
        this Chart3DViewOptionsDialogSession session,
        Chart3DViewOptionsDialogTestSettings settings) => Overlay(session.BuildDialogPlan(), Values(
            Text(ChartOptionsDialogFieldId.RotationX, session.Format(settings.RotationX)),
            Text(ChartOptionsDialogFieldId.RotationY, session.Format(settings.RotationY)),
            Text(ChartOptionsDialogFieldId.Perspective, session.Format(settings.Perspective)),
            Text(ChartOptionsDialogFieldId.HeightPercent, session.Format(settings.HeightPercent)),
            Text(ChartOptionsDialogFieldId.DepthPercent, session.Format(settings.DepthPercent)),
            Text(ChartOptionsDialogFieldId.BarGapDepthPercent, session.Format(settings.BarGapDepthPercent)),
            Choice(ChartOptionsDialogFieldId.RightAngleAxes, session.FindBooleanIndex(settings.RightAngleAxes)),
            Choice(ChartOptionsDialogFieldId.Wireframe, session.FindBooleanIndex(settings.Wireframe))));

    public static ChartOptionsDialogValues BuildTestValues(
        this ChartTextOptionsDialogSession session,
        ChartTextOptionsDialogTestSettings settings) => Overlay(session.BuildDialogPlan(), Values(
            Text(ChartOptionsDialogFieldId.FontFamily, settings.FontFamily),
            Text(ChartOptionsDialogFieldId.FontSize, session.FormatFontSize(settings.FontSizePt)),
            Choice(ChartOptionsDialogFieldId.Bold, session.FindBooleanIndex(settings.Bold)),
            Choice(ChartOptionsDialogFieldId.Italic, session.FindBooleanIndex(settings.Italic)),
            Text(ChartOptionsDialogFieldId.TextColor, settings.Color)));

    public static ChartOptionsDialogValues BuildTestValues(
        this ChartDataTableOptionsDialogSession session,
        ChartDataTableOptionsDialogTestSettings settings,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(culture);
        return Overlay(session.BuildDialogPlan(culture), Values(
            Checked(ChartOptionsDialogFieldId.ShowDataTable, settings.ShowDataTable),
            Checked(ChartOptionsDialogFieldId.HorizontalBorder, settings.ShowHorizontalBorder),
            Checked(ChartOptionsDialogFieldId.VerticalBorder, settings.ShowVerticalBorder),
            Checked(ChartOptionsDialogFieldId.OutlineBorder, settings.ShowOutlineBorder),
            Checked(ChartOptionsDialogFieldId.LegendKeys, settings.ShowLegendKeys),
            Text(ChartOptionsDialogFieldId.BackgroundColor, settings.BackgroundColor),
            Text(ChartOptionsDialogFieldId.BorderColor, settings.BorderColor),
            Text(ChartOptionsDialogFieldId.BorderWidth, Format(settings.BorderWidthPt, culture)),
            Text(ChartOptionsDialogFieldId.TextColor, settings.TextColor),
            Text(ChartOptionsDialogFieldId.FontSize, Format(settings.FontSizePt, culture)),
            Text(ChartOptionsDialogFieldId.FontFamily, settings.FontFamily),
            Checked(ChartOptionsDialogFieldId.Bold, settings.Bold),
            Checked(ChartOptionsDialogFieldId.Italic, settings.Italic)));
    }

    public static ChartOptionsDialogValues BuildTestValues(
        this ChartAxisOptionsDialogSession session,
        ChartAxisOptionsDialogTestSettings settings) => Overlay(session.BuildDialogPlan(), Values(
            Choice(ChartOptionsDialogFieldId.Axis, (int)settings.Axis),
            Text(ChartOptionsDialogFieldId.AxisTitle, settings.Title),
            Checked(ChartOptionsDialogFieldId.ShowAxis, settings.ShowAxis),
            Text(ChartOptionsDialogFieldId.Minimum, session.Format(settings.Minimum)),
            Text(ChartOptionsDialogFieldId.Maximum, session.Format(settings.Maximum)),
            Text(ChartOptionsDialogFieldId.MajorUnit, session.Format(settings.MajorUnit)),
            Text(ChartOptionsDialogFieldId.MinorUnit, session.Format(settings.MinorUnit)),
            Text(ChartOptionsDialogFieldId.NumberFormat, settings.NumberFormatCode),
            Checked(ChartOptionsDialogFieldId.MajorGridlines, settings.MajorGridlines),
            Checked(ChartOptionsDialogFieldId.MinorGridlines, settings.MinorGridlines),
            Choice(ChartOptionsDialogFieldId.MajorTickMark, session.FindTickMarkIndex(settings.MajorTickMark)),
            Choice(ChartOptionsDialogFieldId.MinorTickMark, session.FindTickMarkIndex(settings.MinorTickMark)),
            Choice(ChartOptionsDialogFieldId.TickLabelPosition, session.FindTickLabelPositionIndex(settings.TickLabelPosition)),
            Choice(ChartOptionsDialogFieldId.Crossing, session.FindCrossingIndex(settings.Crosses)),
            Text(ChartOptionsDialogFieldId.CrossesAt, session.Format(settings.CrossesAt)),
            Choice(ChartOptionsDialogFieldId.CrossBetween, session.FindCrossBetweenIndex(settings.CrossBetween)),
            Choice(ChartOptionsDialogFieldId.LabelAlignment, session.FindLabelAlignmentIndex(settings.LabelAlignment)),
            Text(ChartOptionsDialogFieldId.LabelOffset, session.Format(settings.LabelOffsetPercent)),
            Choice(ChartOptionsDialogFieldId.MultiLevelLabels, session.FindMultiLevelLabelsIndex(settings.NoMultiLevelLabels)),
            Choice(ChartOptionsDialogFieldId.AutoCrossing, session.FindAutoCrossingIndex(settings.AutoCrossing)),
            Checked(ChartOptionsDialogFieldId.ReverseOrder, settings.ReverseOrder)));

    public static ChartOptionsDialogValues BuildTestValues(
        this ChartPointOptionsDialogSession session,
        ChartPointOptionsDialogTestSettings settings) => Overlay(session.BuildDialogPlan(), Values(
            Choice(ChartOptionsDialogFieldId.Series, settings.SeriesIndex),
            Choice(ChartOptionsDialogFieldId.Point, settings.PointIndex),
            Text(ChartOptionsDialogFieldId.FillColor, settings.FillColor),
            Text(ChartOptionsDialogFieldId.StrokeColor, settings.StrokeColor),
            Text(ChartOptionsDialogFieldId.StrokeWidth, session.Format(settings.StrokeWidthPt)),
            Checked(ChartOptionsDialogFieldId.UsePointDataLabels, settings.UsePointDataLabels),
            Checked(ChartOptionsDialogFieldId.ValueLabels, settings.ShowValueLabels),
            Checked(ChartOptionsDialogFieldId.PercentLabels, settings.ShowPercentLabels),
            Checked(ChartOptionsDialogFieldId.CategoryLabels, settings.ShowCategoryLabels),
            Checked(ChartOptionsDialogFieldId.SeriesLabels, settings.ShowSeriesLabels),
            Checked(ChartOptionsDialogFieldId.LegendKeys, settings.ShowLegendKeys),
            Checked(ChartOptionsDialogFieldId.BubbleSizeLabels, settings.ShowBubbleSize),
            Checked(ChartOptionsDialogFieldId.LeaderLines, settings.ShowLeaderLines),
            Choice(ChartOptionsDialogFieldId.LabelPosition, session.FindLabelPositionIndex(settings.LabelPosition)),
            Text(ChartOptionsDialogFieldId.LabelNumberFormat, settings.LabelNumberFormat),
            Text(ChartOptionsDialogFieldId.LabelSeparator, settings.LabelSeparator),
            Text(ChartOptionsDialogFieldId.LabelFontFamily, settings.LabelFontFamily),
            Text(ChartOptionsDialogFieldId.LabelFontSize, session.Format(settings.LabelFontSizePt)),
            Checked(ChartOptionsDialogFieldId.LabelBold, settings.LabelBold),
            Checked(ChartOptionsDialogFieldId.LabelItalic, settings.LabelItalic),
            Text(ChartOptionsDialogFieldId.LabelColor, settings.LabelColor),
            Choice(ChartOptionsDialogFieldId.Marker, session.FindMarkerIndex(settings.MarkerSymbol)),
            Text(ChartOptionsDialogFieldId.MarkerSize, session.Format(settings.MarkerSizePt)),
            Text(ChartOptionsDialogFieldId.Explosion, session.Format(settings.ExplosionPercent))));

    public static ChartOptionsDialogValues BuildTestValues(
        this ChartSeriesOptionsDialogSession session,
        ChartSeriesOptionsDialogTestSettings settings)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        var values = new Dictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue>
        {
            [ChartOptionsDialogFieldId.Series] = Selection(settings.SeriesIndex),
            [ChartOptionsDialogFieldId.SmoothLine] = Check(settings.SmoothLine),
            [ChartOptionsDialogFieldId.SecondaryAxis] = Check(settings.OnSecondaryAxis),
            [ChartOptionsDialogFieldId.SeriesChartType] = Selection(session.FindSeriesChartTypeIndex(settings.OverrideChartType)),
            [ChartOptionsDialogFieldId.LineWidth] = TextValue(session.Format(settings.LineWidthPt)),
            [ChartOptionsDialogFieldId.LineColor] = TextValue(settings.LineColor),
            [ChartOptionsDialogFieldId.LineDash] = Selection(session.FindDashIndex(settings.LineDash)),
            [ChartOptionsDialogFieldId.NoLine] = Check(settings.NoLine),
            [ChartOptionsDialogFieldId.FillColor] = TextValue(settings.FillColor),
            [ChartOptionsDialogFieldId.Marker] = Selection(session.FindMarkerIndex(settings.MarkerSymbol)),
            [ChartOptionsDialogFieldId.MarkerSize] = TextValue(session.Format(settings.MarkerSizePt)),
            [ChartOptionsDialogFieldId.UseSeriesDataLabels] = Check(settings.UseSeriesDataLabels),
            [ChartOptionsDialogFieldId.ValueLabels] = Check(settings.ShowValueLabels),
            [ChartOptionsDialogFieldId.PercentLabels] = Check(settings.ShowPercentLabels),
            [ChartOptionsDialogFieldId.CategoryLabels] = Check(settings.ShowCategoryLabels),
            [ChartOptionsDialogFieldId.SeriesLabels] = Check(settings.ShowSeriesLabels),
            [ChartOptionsDialogFieldId.LegendKeys] = Check(settings.ShowLegendKeys),
            [ChartOptionsDialogFieldId.BubbleSizeLabels] = Check(settings.ShowBubbleSize),
            [ChartOptionsDialogFieldId.LeaderLines] = Check(settings.ShowLeaderLines),
            [ChartOptionsDialogFieldId.ErrorBars] = Check(settings.ErrorBars),
            [ChartOptionsDialogFieldId.Trendline] = Check(settings.Trendline),
            [ChartOptionsDialogFieldId.TrendlineType] = Selection(session.FindTrendlineTypeIndex(settings.TrendlineType)),
            [ChartOptionsDialogFieldId.TrendlineOrder] = TextValue(session.Format(settings.TrendlineOrder)),
            [ChartOptionsDialogFieldId.TrendlinePeriod] = TextValue(session.Format(settings.TrendlinePeriod)),
            [ChartOptionsDialogFieldId.TrendlineForward] = TextValue(session.Format(settings.TrendlineForward)),
            [ChartOptionsDialogFieldId.TrendlineBackward] = TextValue(session.Format(settings.TrendlineBackward)),
            [ChartOptionsDialogFieldId.TrendlineEquation] = Check(settings.TrendlineEquation),
            [ChartOptionsDialogFieldId.TrendlineRSquared] = Check(settings.TrendlineRSquared),
            [ChartOptionsDialogFieldId.LabelPosition] = Selection(session.FindLabelPositionIndex(settings.LabelPosition)),
            [ChartOptionsDialogFieldId.LabelNumberFormat] = TextValue(settings.LabelNumberFormat),
            [ChartOptionsDialogFieldId.LabelSeparator] = TextValue(settings.LabelSeparator),
            [ChartOptionsDialogFieldId.LabelFontFamily] = TextValue(settings.LabelFontFamily),
            [ChartOptionsDialogFieldId.LabelFontSize] = TextValue(session.Format(settings.LabelFontSizePt)),
            [ChartOptionsDialogFieldId.LabelBold] = Check(settings.LabelBold),
            [ChartOptionsDialogFieldId.LabelItalic] = Check(settings.LabelItalic),
            [ChartOptionsDialogFieldId.LabelColor] = TextValue(settings.LabelColor),
        };
        if (settings.InvertIfNegative.HasValue)
            values[ChartOptionsDialogFieldId.InvertIfNegative] = Check(settings.InvertIfNegative);
        return Overlay(session.BuildDialogPlan(), new ChartOptionsDialogValues(values));
    }

    public static ChartOptionsDialogValues BuildTestValues(
        this ChartDisplayOptionsDialogSession session,
        ChartDisplayOptionsDialogTestSettings settings)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        var values = new Dictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue>();
        AddText(values, ChartOptionsDialogFieldId.ChartTitle, settings.Title);
        AddCheck(values, ChartOptionsDialogFieldId.TitleOverlay, settings.TitleOverlay);
        AddChoice(values, ChartOptionsDialogFieldId.TitlePosition, settings.TitlePosition, session.FindTitlePositionIndex);
        AddChoice(values, ChartOptionsDialogFieldId.TitleAlignment, settings.TitleAlignment, session.FindTitleAlignmentIndex);
        AddCheck(values, ChartOptionsDialogFieldId.PlotVisibleOnly, settings.PlotVisibleOnly);
        AddCheck(values, ChartOptionsDialogFieldId.RoundedCorners, settings.RoundedCorners);
        AddChoice(values, ChartOptionsDialogFieldId.ChartStyle, settings.StyleId, value => session.FindStyleIndex(value));
        AddChoice(values, ChartOptionsDialogFieldId.Legend, settings.Legend, value => session.FindLegendIndex(value));
        AddCheck(values, ChartOptionsDialogFieldId.ValueLabels, settings.ShowValueLabels);
        AddCheck(values, ChartOptionsDialogFieldId.PercentLabels, settings.ShowPercentLabels);
        AddCheck(values, ChartOptionsDialogFieldId.CategoryLabels, settings.ShowCategoryLabels);
        AddCheck(values, ChartOptionsDialogFieldId.SeriesLabels, settings.ShowSeriesLabels);
        AddCheck(values, ChartOptionsDialogFieldId.LegendKeys, settings.ShowLegendKeys);
        AddCheck(values, ChartOptionsDialogFieldId.BubbleSizeLabels, settings.ShowBubbleSize);
        AddCheck(values, ChartOptionsDialogFieldId.LeaderLines, settings.ShowLeaderLines);
        AddText(values, ChartOptionsDialogFieldId.LabelNumberFormat, settings.LabelNumberFormat);
        AddText(values, ChartOptionsDialogFieldId.LabelSeparator, settings.LabelSeparator);
        AddText(values, ChartOptionsDialogFieldId.LabelFontFamily, settings.LabelFontFamily);
        AddText(values, ChartOptionsDialogFieldId.LabelFontSize, settings.LabelFontSizePt, session.Format);
        AddCheck(values, ChartOptionsDialogFieldId.LabelBold, settings.LabelBold);
        AddCheck(values, ChartOptionsDialogFieldId.LabelItalic, settings.LabelItalic);
        AddText(values, ChartOptionsDialogFieldId.LabelColor, settings.LabelColor);
        AddChoice(values, ChartOptionsDialogFieldId.LabelPosition, settings.LabelPosition, session.FindLabelPositionIndex);
        AddCheck(values, ChartOptionsDialogFieldId.CategoryGridlines, settings.CategoryGridlines);
        AddCheck(values, ChartOptionsDialogFieldId.ValueGridlines, settings.ValueGridlines);
        AddText(values, ChartOptionsDialogFieldId.BarGapWidth, settings.BarGapWidthPercent, session.Format);
        AddText(values, ChartOptionsDialogFieldId.BarOverlap, settings.BarOverlapPercent, session.Format);
        AddChoice(values, ChartOptionsDialogFieldId.DisplayBlanks, settings.DisplayBlanksAs, value => session.FindDisplayBlanksIndex(value));
        AddCheck(values, ChartOptionsDialogFieldId.ShowDataLabelsOverMaximum, settings.ShowDataLabelsOverMaximum);
        AddCheck(values, ChartOptionsDialogFieldId.VaryColors, settings.VaryColors);
        AddCheck(values, ChartOptionsDialogFieldId.LegendOverlay, settings.LegendOverlay);
        AddCheck(values, ChartOptionsDialogFieldId.HighLowLines, settings.HighLowLines);
        AddCheck(values, ChartOptionsDialogFieldId.WaterfallConnectorLines, settings.WaterfallConnectorLines);
        AddCheck(values, ChartOptionsDialogFieldId.DropLines, settings.DropLines);
        AddCheck(values, ChartOptionsDialogFieldId.UpDownBars, settings.UpDownBars);
        AddCheck(values, ChartOptionsDialogFieldId.SeriesLines, settings.SeriesLines);
        return Overlay(session.BuildDialogPlan(), new ChartOptionsDialogValues(values));
    }

    public static Chart3DViewOptions BuildCommitPlanForTests(
        this Chart3DViewOptionsDialogSession session,
        ChartOptionsDialogValues values) => session.BuildCommitPlan(session.BuildInput(values));

    public static ChartAreaOptions BuildCommitPlanForTests(
        this ChartAreaOptionsDialogSession session,
        ChartOptionsDialogValues values,
        CultureInfo culture) => session.BuildCommitPlan(session.BuildInput(values), culture);

    public static ChartAxisOptions BuildCommitPlanForTests(
        this ChartAxisOptionsDialogSession session,
        ChartOptionsDialogValues values) => session.BuildCommitPlan(session.BuildInput(values));

    public static ChartBubbleOptions BuildCommitPlanForTests(
        this ChartBubbleOptionsDialogSession session,
        ChartOptionsDialogValues values) => session.BuildCommitPlan(session.BuildInput(values));

    public static ChartDataTableOptions BuildCommitPlanForTests(
        this ChartDataTableOptionsDialogSession session,
        ChartOptionsDialogValues values,
        CultureInfo culture) => session.BuildCommitPlan(session.BuildInput(values), culture);

    public static ChartDisplayOptions BuildCommitPlanForTests(
        this ChartDisplayOptionsDialogSession session,
        ChartOptionsDialogValues values) => session.BuildCommitPlan(session.BuildInput(values));

    public static ChartLayoutOptions BuildCommitPlanForTests(
        this ChartLayoutOptionsDialogSession session,
        ChartOptionsDialogValues values,
        CultureInfo culture) => session.BuildCommitPlan(session.BuildInput(values), culture);

    public static ChartPieOptions BuildCommitPlanForTests(
        this ChartPieOptionsDialogSession session,
        ChartOptionsDialogValues values,
        CultureInfo culture) => session.BuildCommitPlan(session.BuildInput(values), culture);

    public static ChartPlotStyleOptions BuildCommitPlanForTests(
        this ChartPlotStyleOptionsDialogSession session,
        ChartOptionsDialogValues values) => session.BuildCommitPlan(session.BuildInput(values));

    public static ChartPointOptions BuildCommitPlanForTests(
        this ChartPointOptionsDialogSession session,
        ChartOptionsDialogValues values) => session.BuildCommitPlan(session.BuildInput(values));

    public static ChartProtectionOptions BuildCommitPlanForTests(
        this ChartProtectionOptionsDialogSession session,
        ChartOptionsDialogValues values) => session.BuildCommitPlan(session.BuildInput(values));

    public static ChartSeriesOptions BuildCommitPlanForTests(
        this ChartSeriesOptionsDialogSession session,
        ChartOptionsDialogValues values) => session.BuildCommitPlan(session.BuildInput(values));

    public static ChartTextOptions BuildCommitPlanForTests(
        this ChartTextOptionsDialogSession session,
        ChartOptionsDialogValues values) => session.BuildCommitPlan(session.BuildInput(values));

    private static ChartOptionsDialogValues Values(
        params (ChartOptionsDialogFieldId Id, ChartOptionsDialogFieldValue Value)[] values) => new(
            values.ToDictionary(item => item.Id, item => item.Value));

    private static ChartOptionsDialogValues Overlay(
        ChartOptionsDialogPlan plan,
        ChartOptionsDialogValues updates)
    {
        var values = plan.Fields.ToDictionary(
            pair => pair.Key,
            pair => new ChartOptionsDialogFieldValue(
                pair.Value.Text,
                pair.Value.SelectedIndex,
                pair.Value.IsChecked));
        foreach (var (fieldId, value) in updates.Fields)
            values[fieldId] = value;
        return new ChartOptionsDialogValues(values);
    }

    private static (ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue) Text(
        ChartOptionsDialogFieldId id,
        string? value) => (id, TextValue(value));

    private static (ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue) Choice(
        ChartOptionsDialogFieldId id,
        int value) => (id, Selection(value));

    private static (ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue) Checked(
        ChartOptionsDialogFieldId id,
        bool? value) => (id, Check(value));

    private static ChartOptionsDialogFieldValue TextValue(string? value) =>
        new(Text: value ?? string.Empty);

    private static ChartOptionsDialogFieldValue Selection(int value) =>
        new(SelectedIndex: value);

    private static ChartOptionsDialogFieldValue Check(bool? value) =>
        new(IsChecked: value);

    private static string Format(double? value, CultureInfo culture) =>
        ChartDialogOptionProjection.Format(value, culture);

    private static string Format(int? value, CultureInfo culture) =>
        ChartDialogOptionProjection.Format(value, culture);

    private static int ModeIndex(ChartManualLayoutMode value) => FindIndex(
        ChartLayoutOptionsPlanner.ModeOptions, value, option => option.Value);

    private static int FindIndex<TOption, TValue>(
        IReadOnlyList<TOption> options,
        TValue value,
        Func<TOption, TValue> selector,
        IEqualityComparer<TValue>? comparer = null) =>
        ChartDialogOptionProjection.FindIndex(options, value, selector, comparer: comparer);

    private static void AddText(
        IDictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue> values,
        ChartOptionsDialogFieldId id,
        string? value)
    {
        if (value is not null)
            values[id] = TextValue(value);
    }

    private static void AddText<T>(
        IDictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue> values,
        ChartOptionsDialogFieldId id,
        T? value,
        Func<T?, string> format)
        where T : struct
    {
        if (value.HasValue)
            values[id] = TextValue(format(value));
    }

    private static void AddCheck(
        IDictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue> values,
        ChartOptionsDialogFieldId id,
        bool? value)
    {
        if (value.HasValue)
            values[id] = Check(value);
    }

    private static void AddChoice<T>(
        IDictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue> values,
        ChartOptionsDialogFieldId id,
        T? value,
        Func<T, int> findIndex)
        where T : struct
    {
        if (value.HasValue)
            values[id] = Selection(findIndex(value.Value));
    }
}
