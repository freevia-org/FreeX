using System.Collections.ObjectModel;
using System.Globalization;
using Free.Shared.Shell;

namespace FreeP.App.Compositor;

public enum ChartOptionsDialogActionId
{
    Accept,
    Cancel,
}

public enum ChartOptionsDialogFieldId
{
    RotationX,
    RotationY,
    Perspective,
    HeightPercent,
    DepthPercent,
    BarGapDepthPercent,
    RightAngleAxes,
    Wireframe,
    BubbleScale,
    BubbleSizeRepresents,
    ShowNegativeBubbles,
    ScatterStyle,
    RadarStyle,
    ProtectedChartObject,
    ProtectedData,
    ProtectedFormatting,
    ProtectedSelection,
    FontFamily,
    FontSize,
    Bold,
    Italic,
    TextColor,
    AreaTarget,
    FillColor,
    FillTransparency,
    NoFill,
    OutlineColor,
    NoOutline,
    OutlineWidth,
    ShowDataTable,
    HorizontalBorder,
    VerticalBorder,
    OutlineBorder,
    LegendKeys,
    BackgroundColor,
    BorderColor,
    BorderWidth,
    FirstSliceAngle,
    DoughnutHole,
    OfPieType,
    OfPieSplitType,
    OfPieSplitPosition,
    OfPieSecondPieSize,
    OfPieCustomPointIndices,
    OfPieGapWidth,
    OfPieSeriesLines,
    LayoutTargetObject,
    LayoutTarget,
    XMode,
    YMode,
    WidthMode,
    HeightMode,
    X,
    Y,
    Width,
    Height,
    ChartExSeries,
    ChartExLayout,
    Axis,
    AxisTitle,
    AxisTitleFontFamily,
    AxisTitleFontSize,
    AxisTitleColor,
    AxisTitleBold,
    AxisTitleItalic,
    ShowAxis,
    Minimum,
    Maximum,
    MajorUnit,
    MinorUnit,
    NumberFormat,
    DisplayUnit,
    CustomDisplayUnit,
    MajorGridlines,
    MinorGridlines,
    MajorTickMark,
    MinorTickMark,
    TickLabelPosition,
    Crossing,
    CrossesAt,
    CrossBetween,
    LabelAlignment,
    LabelOffset,
    MultiLevelLabels,
    AutoCrossing,
    ReverseOrder,
    ChartTitle,
    TitleOverlay,
    TitlePosition,
    TitleAlignment,
    PlotVisibleOnly,
    RoundedCorners,
    ChartStyle,
    Legend,
    ValueLabels,
    PercentLabels,
    CategoryLabels,
    SeriesLabels,
    BubbleSizeLabels,
    LeaderLines,
    LabelNumberFormat,
    LabelSeparator,
    LabelFontFamily,
    LabelFontSize,
    LabelBold,
    LabelItalic,
    LabelColor,
    LabelPosition,
    CategoryGridlines,
    ValueGridlines,
    BarGapWidth,
    BarOverlap,
    DisplayBlanks,
    ShowDataLabelsOverMaximum,
    VaryColors,
    LegendOverlay,
    HighLowLines,
    WaterfallConnectorLines,
    DropLines,
    UpDownBars,
    SeriesLines,
    Series,
    Point,
    StrokeColor,
    StrokeWidth,
    UsePointDataLabels,
    Marker,
    MarkerSize,
    Explosion,
    SeriesChartType,
    SmoothLine,
    SecondaryAxis,
    InvertIfNegative,
    LineWidth,
    LineColor,
    LineDash,
    NoLine,
    UseSeriesDataLabels,
    ErrorBars,
    ErrorDirection,
    ErrorBarType,
    ErrorValueType,
    ErrorValue,
    ErrorNoEndCap,
    Trendline,
    TrendlineType,
    TrendlineOrder,
    TrendlinePeriod,
    TrendlineForward,
    TrendlineBackward,
    TrendlineEquation,
    TrendlineRSquared,
}

public enum ChartOptionsDialogControlKind
{
    Text,
    Choice,
    Toggle,
}

public sealed record ChartOptionsDialogFieldPlan(
    ChartOptionsDialogFieldId Id,
    ChartOptionsDialogControlKind ControlKind,
    string Label,
    string AccessibleName,
    string AutomationId,
    string Text = "",
    int SelectedIndex = -1,
    bool? IsChecked = false,
    IReadOnlyList<string>? Choices = null,
    bool IsVisible = true,
    bool IsEnabled = true,
    double LabelWidth = 180,
    double MinimumControlWidth = 150,
    bool IsStandalone = false,
    bool IsThreeState = false)
{
    public IReadOnlyList<string> ChoiceLabels { get; } =
        Choices ?? Array.Empty<string>();
}

public sealed record ChartOptionsDialogGroupPlan(
    string Id,
    string? Header,
    string AccessibleName,
    IReadOnlyList<ChartOptionsDialogFieldPlan> Fields);

public sealed class ChartOptionsDialogPlan
{
    private readonly IReadOnlyDictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldPlan> _fields;

    public ChartOptionsDialogPlan(
        string commandId,
        string title,
        double width,
        double height,
        double minimumWidth,
        double minimumHeight,
        bool isResizable,
        bool isScrollable,
        string? hint,
        string acceptLabel,
        string cancelLabel,
        IReadOnlyList<ChartOptionsDialogGroupPlan> groups)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(acceptLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(cancelLabel);
        ArgumentNullException.ThrowIfNull(groups);

        var fields = groups.SelectMany(group => group.Fields).ToArray();
        var duplicate = fields
            .GroupBy(field => field.Id)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate chart dialog field: {duplicate.Key}.", nameof(groups));

        CommandId = commandId;
        Title = title;
        Width = width;
        Height = height;
        MinimumWidth = minimumWidth;
        MinimumHeight = minimumHeight;
        IsResizable = isResizable;
        IsScrollable = isScrollable;
        Hint = hint;
        AcceptLabel = acceptLabel;
        CancelLabel = cancelLabel;
        var automationToken = AutomationIdToken.KeepLettersAndDigits(commandId);
        AcceptAction = new(
            ChartOptionsDialogActionId.Accept,
            acceptLabel,
            $"Apply {title}",
            $"FreeP.ChartOptions.{automationToken}.Accept",
            IsDefault: true);
        CancelAction = new(
            ChartOptionsDialogActionId.Cancel,
            cancelLabel,
            $"Cancel {title}",
            $"FreeP.ChartOptions.{automationToken}.Cancel",
            IsCancel: true);
        Groups = new ReadOnlyCollection<ChartOptionsDialogGroupPlan>(groups.ToArray());
        _fields = new ReadOnlyDictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldPlan>(
            fields.ToDictionary(field => field.Id));
    }

    public string CommandId { get; }
    public string Title { get; }
    public double Width { get; }
    public double Height { get; }
    public double MinimumWidth { get; }
    public double MinimumHeight { get; }
    public bool IsResizable { get; }
    public bool IsScrollable { get; }
    public string? Hint { get; }
    public string AcceptLabel { get; }
    public string CancelLabel { get; }
    public PresentationDialogActionPlan<ChartOptionsDialogActionId> AcceptAction { get; }
    public PresentationDialogActionPlan<ChartOptionsDialogActionId> CancelAction { get; }
    public IReadOnlyList<ChartOptionsDialogGroupPlan> Groups { get; }
    public IReadOnlyDictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldPlan> Fields => _fields;

    public ChartOptionsDialogFieldPlan Field(ChartOptionsDialogFieldId fieldId) =>
        _fields.TryGetValue(fieldId, out var field)
            ? field
            : throw new KeyNotFoundException($"The dialog plan does not define {fieldId}.");
}

public sealed class ChartOptionsDialogValues
{
    private readonly IReadOnlyDictionary<ChartOptionsDialogFieldId, PresentationDialogFieldValue> _values;

    public ChartOptionsDialogValues(
        IReadOnlyDictionary<ChartOptionsDialogFieldId, PresentationDialogFieldValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new ReadOnlyDictionary<ChartOptionsDialogFieldId, PresentationDialogFieldValue>(
            new Dictionary<ChartOptionsDialogFieldId, PresentationDialogFieldValue>(values));
    }

    public string Text(ChartOptionsDialogFieldId fieldId) => Value(fieldId).Text;
    public int SelectedIndex(ChartOptionsDialogFieldId fieldId) => Value(fieldId).SelectedIndex;
    public bool IsChecked(ChartOptionsDialogFieldId fieldId) => Value(fieldId).IsChecked == true;
    public bool? NullableChecked(ChartOptionsDialogFieldId fieldId) => Value(fieldId).IsChecked;
    public IReadOnlyDictionary<ChartOptionsDialogFieldId, PresentationDialogFieldValue> Fields => _values;

    private PresentationDialogFieldValue Value(ChartOptionsDialogFieldId fieldId) =>
        _values.TryGetValue(fieldId, out var value)
            ? value
            : throw new KeyNotFoundException($"The dialog values do not contain {fieldId}.");
}

public static partial class ChartOptionsDialogPlanCatalog
{
    public static ChartOptionsDialogPlan BuildDialogPlan(
        this Chart3DViewOptionsDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        var surface = session.Surface;
        var choices = Labels(session.BooleanOptions, option => option.Label);
        return Plan(
            surface.CommandId,
            surface.Title,
            Chart3DViewOptionsPlanner.DefaultDialogWidth,
            Chart3DViewOptionsPlanner.DefaultDialogHeight,
            380,
            320,
            surface.AutoHint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("view", "3-D view", [
                Text(ChartOptionsDialogFieldId.RotationX, surface.RotationXLabel, state.RotationXText, 170),
                Text(ChartOptionsDialogFieldId.RotationY, surface.RotationYLabel, state.RotationYText, 170),
                Text(ChartOptionsDialogFieldId.Perspective, surface.PerspectiveLabel, state.PerspectiveText, 170),
                Text(ChartOptionsDialogFieldId.HeightPercent, surface.HeightPercentLabel, state.HeightPercentText, 170),
                Text(ChartOptionsDialogFieldId.DepthPercent, surface.DepthPercentLabel, state.DepthPercentText, 170),
                Text(ChartOptionsDialogFieldId.BarGapDepthPercent, surface.BarGapDepthPercentLabel, state.BarGapDepthPercentText, 170, isEnabled: state.SupportsBarGapDepth),
                Choice(ChartOptionsDialogFieldId.RightAngleAxes, surface.RightAngleAxesLabel, state.RightAngleAxesIndex, choices, 170),
                Choice(ChartOptionsDialogFieldId.Wireframe, surface.WireframeLabel, state.WireframeIndex, choices, 170),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartBubbleOptionsDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        var surface = session.Surface;
        return Plan(
            surface.CommandId,
            surface.Title,
            ChartBubbleOptionsPlanner.DefaultDialogWidth,
            ChartBubbleOptionsPlanner.DefaultDialogHeight,
            360,
            240,
            surface.Hint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("bubble", null, [
                Text(ChartOptionsDialogFieldId.BubbleScale, surface.BubbleScaleLabel, state.BubbleScaleText, 190),
                Choice(
                    ChartOptionsDialogFieldId.BubbleSizeRepresents,
                    surface.SizeRepresentsLabel,
                    state.SizeRepresentsIndex,
                    Labels(session.SizeRepresentsOptions, option => option.Label),
                    190),
                Toggle(ChartOptionsDialogFieldId.ShowNegativeBubbles, surface.ShowNegativeBubblesLabel, state.ShowNegativeBubbles),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartPlotStyleOptionsDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        var surface = session.Surface;
        return Plan(
            surface.CommandId,
            surface.Title,
            ChartPlotStyleOptionsPlanner.DefaultDialogWidth,
            ChartPlotStyleOptionsPlanner.DefaultDialogHeight,
            360,
            220,
            surface.Hint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("plot-style", null, [
                Choice(ChartOptionsDialogFieldId.ScatterStyle, surface.ScatterStyleLabel, state.ScatterStyleIndex, Labels(session.ScatterStyleOptions, option => option.Label), 190, isEnabled: state.IsScatterEnabled),
                Choice(ChartOptionsDialogFieldId.RadarStyle, surface.RadarStyleLabel, state.RadarStyleIndex, Labels(session.RadarStyleOptions, option => option.Label), 190, isEnabled: state.IsRadarEnabled),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartProtectionOptionsDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        var surface = session.Surface;
        var choices = Labels(session.BooleanOptions, option => option.Label);
        return Plan(
            surface.CommandId,
            surface.Title,
            ChartProtectionOptionsPlanner.DefaultDialogWidth,
            ChartProtectionOptionsPlanner.DefaultDialogHeight,
            400,
            260,
            surface.Hint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("protection", "Protection", [
                Choice(ChartOptionsDialogFieldId.ProtectedChartObject, surface.ChartObjectLabel, state.ChartObjectIndex, choices),
                Choice(ChartOptionsDialogFieldId.ProtectedData, surface.DataLabel, state.DataIndex, choices),
                Choice(ChartOptionsDialogFieldId.ProtectedFormatting, surface.FormattingLabel, state.FormattingIndex, choices),
                Choice(ChartOptionsDialogFieldId.ProtectedSelection, surface.SelectionLabel, state.SelectionIndex, choices),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartTextOptionsDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        var surface = session.Surface;
        var choices = Labels(session.BooleanOptions, option => option.Label);
        return Plan(
            surface.CommandId,
            surface.Title,
            ChartTextOptionsPlanner.DefaultDialogWidth,
            ChartTextOptionsPlanner.DefaultDialogHeight,
            380,
            280,
            surface.AutoHint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("text", "Text", [
                Text(ChartOptionsDialogFieldId.FontFamily, surface.FontFamilyLabel, state.FontFamilyText),
                Text(ChartOptionsDialogFieldId.FontSize, surface.FontSizeLabel, state.FontSizeText),
                Choice(ChartOptionsDialogFieldId.Bold, surface.BoldLabel, state.BoldIndex, choices),
                Choice(ChartOptionsDialogFieldId.Italic, surface.ItalicLabel, state.ItalicIndex, choices),
                Text(ChartOptionsDialogFieldId.TextColor, surface.ColorLabel, state.ColorText),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartAreaOptionsDialogSession session,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        culture ??= CultureInfo.CurrentCulture;
        var state = session.State;
        var surface = ChartAreaOptionsPlanner.BuildSurfacePlan();
        return Plan(
            surface.CommandId,
            surface.Title,
            400,
            360,
            390,
            300,
            surface.Hint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("target", null, [
                Choice(ChartOptionsDialogFieldId.AreaTarget, surface.TargetLabel, state.TargetIndex, Labels(ChartAreaOptionsPlanner.TargetOptions, option => option.Label), 170),
            ]),
            Group("fill", "Fill", [
                Text(ChartOptionsDialogFieldId.FillColor, surface.FillLabel, state.FillColor, 170),
                Text(ChartOptionsDialogFieldId.FillTransparency, surface.FillTransparencyLabel, Format(state.FillTransparencyPercent, culture), 170),
                Toggle(ChartOptionsDialogFieldId.NoFill, surface.NoFillLabel, state.NoFill),
            ]),
            Group("outline", "Outline", [
                Text(ChartOptionsDialogFieldId.OutlineColor, surface.OutlineLabel, state.OutlineColor, 170),
                Text(ChartOptionsDialogFieldId.OutlineWidth, surface.WidthLabel, Format(state.OutlineWidthPt, culture), 170),
                Toggle(ChartOptionsDialogFieldId.NoOutline, surface.NoOutlineLabel, state.NoOutline),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartDataTableOptionsDialogSession session,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        culture ??= CultureInfo.CurrentCulture;
        var state = session.State;
        var surface = ChartDataTableOptionsPlanner.BuildSurfacePlan();
        return ScrollablePlan(
            surface.CommandId,
            surface.Title,
            ChartDataTableOptionsPlanner.DefaultDialogWidth,
            ChartDataTableOptionsPlanner.DefaultDialogHeight,
            360,
            500,
            hint: null,
            surface.OkLabel,
            surface.CancelLabel,
            Group("table", "Data table", [
                Toggle(ChartOptionsDialogFieldId.ShowDataTable, surface.ShowDataTableLabel, state.ShowDataTable),
                Toggle(ChartOptionsDialogFieldId.HorizontalBorder, surface.HorizontalBorderLabel, state.ShowHorizontalBorder),
                Toggle(ChartOptionsDialogFieldId.VerticalBorder, surface.VerticalBorderLabel, state.ShowVerticalBorder),
                Toggle(ChartOptionsDialogFieldId.OutlineBorder, surface.OutlineBorderLabel, state.ShowOutlineBorder),
                Toggle(ChartOptionsDialogFieldId.LegendKeys, surface.LegendKeysLabel, state.ShowLegendKeys),
            ]),
            Group("appearance", "Appearance", [
                Text(ChartOptionsDialogFieldId.BackgroundColor, surface.BackgroundColorLabel, state.BackgroundColor),
                Text(ChartOptionsDialogFieldId.BorderColor, surface.BorderColorLabel, state.BorderColor),
                Text(ChartOptionsDialogFieldId.BorderWidth, surface.BorderWidthLabel, Format(state.BorderWidthPt, culture, "0.###")),
            ]),
            Group("table-text", "Text", [
                Text(ChartOptionsDialogFieldId.TextColor, surface.TextColorLabel, state.TextColor),
                Text(ChartOptionsDialogFieldId.FontSize, surface.FontSizeLabel, Format(state.FontSizePt, culture, "0.###")),
                Text(ChartOptionsDialogFieldId.FontFamily, surface.FontFamilyLabel, state.FontFamily),
                Toggle(ChartOptionsDialogFieldId.Bold, surface.BoldLabel, state.Bold, isThreeState: true),
                Toggle(ChartOptionsDialogFieldId.Italic, surface.ItalicLabel, state.Italic, isThreeState: true),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartLayoutOptionsDialogSession session,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        culture ??= CultureInfo.CurrentCulture;
        var state = session.State;
        var surface = ChartLayoutOptionsPlanner.BuildSurfacePlan();
        var modes = Labels(ChartLayoutOptionsPlanner.ModeOptions, option => option.Label);
        return ScrollablePlan(
            surface.CommandId,
            surface.Title,
            ChartLayoutOptionsPlanner.DefaultDialogWidth,
            ChartLayoutOptionsPlanner.DefaultDialogHeight,
            450,
            430,
            surface.Hint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("layout-target", "Target", [
                Choice(ChartOptionsDialogFieldId.LayoutTargetObject, surface.TargetLabel, state.TargetIndex, Labels(ChartLayoutOptionsPlanner.TargetOptions, option => option.Label), 140),
                Choice(ChartOptionsDialogFieldId.LayoutTarget, surface.LayoutTargetLabel, state.LayoutTargetIndex, Labels(state.LayoutTargetOptions, option => option.Label), 140),
            ]),
            Group("layout-position", "Position and size", [
                Text(ChartOptionsDialogFieldId.X, surface.XLabel, Format(state.X, culture), 140),
                Choice(ChartOptionsDialogFieldId.XMode, surface.XModeLabel, state.XModeIndex, modes, 140),
                Text(ChartOptionsDialogFieldId.Y, surface.YLabel, Format(state.Y, culture), 140),
                Choice(ChartOptionsDialogFieldId.YMode, surface.YModeLabel, state.YModeIndex, modes, 140),
                Text(ChartOptionsDialogFieldId.Width, surface.WidthLabel, Format(state.Width, culture), 140),
                Choice(ChartOptionsDialogFieldId.WidthMode, surface.WidthModeLabel, state.WidthModeIndex, modes, 140),
                Text(ChartOptionsDialogFieldId.Height, surface.HeightLabel, Format(state.Height, culture), 140),
                Choice(ChartOptionsDialogFieldId.HeightMode, surface.HeightModeLabel, state.HeightModeIndex, modes, 140),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartPieOptionsDialogSession session,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        culture ??= CultureInfo.CurrentCulture;
        var state = session.State;
        var surface = ChartPieOptionsPlanner.BuildSurfacePlan();
        var groups = new List<ChartOptionsDialogGroupPlan>
        {
            Group("pie", null, [
                Text(ChartOptionsDialogFieldId.FirstSliceAngle, surface.FirstSliceAngleLabel, Format(state.FirstSliceAngleDegrees ?? 0, culture), 220),
                Text(ChartOptionsDialogFieldId.DoughnutHole, surface.DoughnutHoleLabel, Format(state.DoughnutHolePercent, culture), 220, isEnabled: state.IsDoughnut),
            ]),
        };
        if (state.IsOfPie)
        {
            groups.Add(Group("of-pie", "Secondary plot", [
                Choice(ChartOptionsDialogFieldId.OfPieType, surface.OfPieTypeLabel, state.OfPieTypeIndex, state.OfPieTypeOptions, 220),
                Choice(ChartOptionsDialogFieldId.OfPieSplitType, surface.OfPieSplitTypeLabel, state.OfPieSplitTypeIndex, state.OfPieSplitTypeOptions, 220),
                Text(ChartOptionsDialogFieldId.OfPieSplitPosition, surface.OfPieSplitPositionLabel, Format(state.OfPieSplitPosition ?? 0, culture), 220),
                Text(ChartOptionsDialogFieldId.OfPieSecondPieSize, surface.OfPieSecondPieSizeLabel, Format(state.OfPieSecondPieSizePercent, culture), 220),
                Text(ChartOptionsDialogFieldId.OfPieCustomPointIndices, surface.OfPieCustomPointIndicesLabel, string.Join(",", state.OfPieCustomPointIndices), 220),
                Text(ChartOptionsDialogFieldId.OfPieGapWidth, surface.OfPieGapWidthLabel, Format(state.OfPieGapWidthPercent, culture), 220),
                Toggle(ChartOptionsDialogFieldId.OfPieSeriesLines, surface.OfPieSeriesLinesLabel, state.OfPieSeriesLines),
            ]));
        }

        return state.IsOfPie ? ScrollablePlan(
            surface.CommandId,
            surface.Title,
            ChartPieOptionsPlanner.DefaultDialogWidth,
            state.IsOfPie ? ChartPieOptionsPlanner.DefaultDialogHeight : 250,
            380,
            220,
            surface.Hint,
            surface.OkLabel,
            surface.CancelLabel,
            groups.ToArray()) : Plan(
                surface.CommandId,
                surface.Title,
                ChartPieOptionsPlanner.DefaultDialogWidth,
                250,
                380,
                220,
                surface.Hint,
                surface.OkLabel,
                surface.CancelLabel,
                groups.ToArray());
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartExSeriesLayoutDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var selection = session.Selection;
        return Plan(
            ChartExSeriesLayoutPlanner.CommandId,
            ChartExSeriesLayoutPlanner.DialogTitle,
            430,
            220,
            400,
            200,
            hint: null,
            ChartExSeriesLayoutPlanner.OkLabel,
            ChartExSeriesLayoutPlanner.CancelLabel,
            Group("chartex-layout", null, [
                Choice(ChartOptionsDialogFieldId.ChartExSeries, ChartExSeriesLayoutPlanner.SeriesLabel, selection.SeriesOptionIndex, Labels(session.SeriesOptions, option => option.Label), 80),
                Choice(ChartOptionsDialogFieldId.ChartExLayout, ChartExSeriesLayoutPlanner.LayoutLabel, selection.LayoutIndex, Labels(selection.LayoutChoices, option => option.Label), 80),
            ]));
    }

    public static Chart3DViewOptionsDialogInput BuildInput(
        this Chart3DViewOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.Text(ChartOptionsDialogFieldId.RotationX),
            values.Text(ChartOptionsDialogFieldId.RotationY),
            values.Text(ChartOptionsDialogFieldId.Perspective),
            values.Text(ChartOptionsDialogFieldId.HeightPercent),
            values.Text(ChartOptionsDialogFieldId.DepthPercent),
            values.Text(ChartOptionsDialogFieldId.BarGapDepthPercent),
            values.SelectedIndex(ChartOptionsDialogFieldId.RightAngleAxes),
            values.SelectedIndex(ChartOptionsDialogFieldId.Wireframe));

    public static ChartBubbleOptionsDialogInput BuildInput(
        this ChartBubbleOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.Text(ChartOptionsDialogFieldId.BubbleScale),
            values.SelectedIndex(ChartOptionsDialogFieldId.BubbleSizeRepresents),
            values.IsChecked(ChartOptionsDialogFieldId.ShowNegativeBubbles));

    public static ChartPlotStyleOptionsDialogInput BuildInput(
        this ChartPlotStyleOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.SelectedIndex(ChartOptionsDialogFieldId.ScatterStyle),
            values.SelectedIndex(ChartOptionsDialogFieldId.RadarStyle));

    public static ChartProtectionOptionsDialogInput BuildInput(
        this ChartProtectionOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.SelectedIndex(ChartOptionsDialogFieldId.ProtectedChartObject),
            values.SelectedIndex(ChartOptionsDialogFieldId.ProtectedData),
            values.SelectedIndex(ChartOptionsDialogFieldId.ProtectedFormatting),
            values.SelectedIndex(ChartOptionsDialogFieldId.ProtectedSelection));

    public static ChartTextOptionsDialogInput BuildInput(
        this ChartTextOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.Text(ChartOptionsDialogFieldId.FontFamily),
            values.Text(ChartOptionsDialogFieldId.FontSize),
            values.SelectedIndex(ChartOptionsDialogFieldId.Bold),
            values.SelectedIndex(ChartOptionsDialogFieldId.Italic),
            values.Text(ChartOptionsDialogFieldId.TextColor));

    public static ChartAreaOptionsDialogInput BuildInput(
        this ChartAreaOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.SelectedIndex(ChartOptionsDialogFieldId.AreaTarget),
            values.Text(ChartOptionsDialogFieldId.FillColor),
            values.Text(ChartOptionsDialogFieldId.FillTransparency),
            values.IsChecked(ChartOptionsDialogFieldId.NoFill),
            values.Text(ChartOptionsDialogFieldId.OutlineColor),
            values.IsChecked(ChartOptionsDialogFieldId.NoOutline),
            values.Text(ChartOptionsDialogFieldId.OutlineWidth));

    public static ChartDataTableOptionsDialogInput BuildInput(
        this ChartDataTableOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.IsChecked(ChartOptionsDialogFieldId.ShowDataTable),
            values.IsChecked(ChartOptionsDialogFieldId.HorizontalBorder),
            values.IsChecked(ChartOptionsDialogFieldId.VerticalBorder),
            values.IsChecked(ChartOptionsDialogFieldId.OutlineBorder),
            values.IsChecked(ChartOptionsDialogFieldId.LegendKeys),
            values.Text(ChartOptionsDialogFieldId.BackgroundColor),
            values.Text(ChartOptionsDialogFieldId.BorderColor),
            values.Text(ChartOptionsDialogFieldId.BorderWidth),
            values.Text(ChartOptionsDialogFieldId.TextColor),
            values.Text(ChartOptionsDialogFieldId.FontSize),
            values.Text(ChartOptionsDialogFieldId.FontFamily),
            values.NullableChecked(ChartOptionsDialogFieldId.Bold),
            values.NullableChecked(ChartOptionsDialogFieldId.Italic));

    public static ChartLayoutOptionsDialogInput BuildInput(
        this ChartLayoutOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.SelectedIndex(ChartOptionsDialogFieldId.LayoutTargetObject),
            values.SelectedIndex(ChartOptionsDialogFieldId.LayoutTarget),
            values.SelectedIndex(ChartOptionsDialogFieldId.XMode),
            values.SelectedIndex(ChartOptionsDialogFieldId.YMode),
            values.SelectedIndex(ChartOptionsDialogFieldId.WidthMode),
            values.SelectedIndex(ChartOptionsDialogFieldId.HeightMode),
            values.Text(ChartOptionsDialogFieldId.X),
            values.Text(ChartOptionsDialogFieldId.Y),
            values.Text(ChartOptionsDialogFieldId.Width),
            values.Text(ChartOptionsDialogFieldId.Height));

    public static ChartPieOptionsDialogInput BuildInput(
        this ChartPieOptionsDialogSession session,
        ChartOptionsDialogValues values)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(values);
        var isOfPie = session.State.IsOfPie;
        return new ChartPieOptionsDialogInput(
            values.Text(ChartOptionsDialogFieldId.FirstSliceAngle),
            values.Text(ChartOptionsDialogFieldId.DoughnutHole),
            isOfPie ? values.SelectedIndex(ChartOptionsDialogFieldId.OfPieType) : 0,
            isOfPie ? values.SelectedIndex(ChartOptionsDialogFieldId.OfPieSplitType) : 0,
            isOfPie ? values.Text(ChartOptionsDialogFieldId.OfPieSplitPosition) : null,
            isOfPie ? values.Text(ChartOptionsDialogFieldId.OfPieSecondPieSize) : null,
            isOfPie ? values.Text(ChartOptionsDialogFieldId.OfPieCustomPointIndices) : null,
            isOfPie ? values.Text(ChartOptionsDialogFieldId.OfPieGapWidth) : null,
            isOfPie && values.IsChecked(ChartOptionsDialogFieldId.OfPieSeriesLines));
    }

    private static ChartOptionsDialogPlan Plan(
        string commandId,
        string title,
        double width,
        double height,
        double minimumWidth,
        double minimumHeight,
        string? hint,
        string acceptLabel,
        string cancelLabel,
        params ChartOptionsDialogGroupPlan[] groups) => new(
            commandId,
            title,
            width,
            height,
            minimumWidth,
            minimumHeight,
            isResizable: false,
            isScrollable: false,
            hint,
            acceptLabel,
            cancelLabel,
            groups);

    private static ChartOptionsDialogPlan ScrollablePlan(
        string commandId,
        string title,
        double width,
        double height,
        double minimumWidth,
        double minimumHeight,
        string? hint,
        string acceptLabel,
        string cancelLabel,
        params ChartOptionsDialogGroupPlan[] groups) => new(
            commandId,
            title,
            width,
            height,
            minimumWidth,
            minimumHeight,
            isResizable: false,
            isScrollable: true,
            hint,
            acceptLabel,
            cancelLabel,
            groups);

    private static ChartOptionsDialogGroupPlan Group(
        string id,
        string? header,
        IReadOnlyList<ChartOptionsDialogFieldPlan> fields) =>
        new(id, header, header ?? "Chart options", fields);

    private static ChartOptionsDialogFieldPlan Text(
        ChartOptionsDialogFieldId id,
        string label,
        string? value,
        double labelWidth = 180,
        bool isVisible = true,
        bool isEnabled = true) =>
        Field(id, ChartOptionsDialogControlKind.Text, label, value ?? string.Empty, labelWidth: labelWidth, isVisible: isVisible, isEnabled: isEnabled);

    private static ChartOptionsDialogFieldPlan Choice(
        ChartOptionsDialogFieldId id,
        string label,
        int selectedIndex,
        IReadOnlyList<string> choices,
        double labelWidth = 180,
        bool isVisible = true,
        bool isEnabled = true) =>
        Field(id, ChartOptionsDialogControlKind.Choice, label, selectedIndex: selectedIndex, choices: choices, labelWidth: labelWidth, isVisible: isVisible, isEnabled: isEnabled);

    private static ChartOptionsDialogFieldPlan Toggle(
        ChartOptionsDialogFieldId id,
        string label,
        bool? isChecked,
        bool isVisible = true,
        bool isEnabled = true,
        bool isThreeState = false) =>
        Field(id, ChartOptionsDialogControlKind.Toggle, label, isChecked: isChecked, isVisible: isVisible, isEnabled: isEnabled, isStandalone: true, isThreeState: isThreeState);

    private static ChartOptionsDialogFieldPlan Field(
        ChartOptionsDialogFieldId id,
        ChartOptionsDialogControlKind kind,
        string label,
        string text = "",
        int selectedIndex = -1,
        bool? isChecked = false,
        IReadOnlyList<string>? choices = null,
        double labelWidth = 180,
        bool isVisible = true,
        bool isEnabled = true,
        bool isStandalone = false,
        bool isThreeState = false) => new(
            id,
            kind,
            label,
            label,
            $"FreeP.ChartOptions.{id}",
            text,
            selectedIndex,
            isChecked,
            choices,
            isVisible,
            isEnabled,
            labelWidth,
            MinimumControlWidth: 150,
            IsStandalone: isStandalone,
            IsThreeState: isThreeState);

    private static IReadOnlyList<string> Labels<T>(
        IReadOnlyList<T> options,
        Func<T, string> label) =>
        options.Select(label).ToArray();

    private static string Format(
        double? value,
        CultureInfo culture,
        string? format = null) =>
        ChartDialogOptionProjection.Format(value, culture, format ?? "G");

    private static string Format(int? value, CultureInfo culture) =>
        ChartDialogOptionProjection.Format(value, culture);
}
