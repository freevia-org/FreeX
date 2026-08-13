using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartAxisOptionsDialogState(
    int AxisIndex,
    string Title,
    string TitleFontFamily,
    string TitleFontSizeText,
    string TitleColor,
    bool? TitleBold,
    bool? TitleItalic,
    bool ShowAxis,
    string MinimumText,
    string MaximumText,
    string MajorUnitText,
    string MinorUnitText,
    string NumberFormatCode,
    int DisplayUnitIndex,
    string CustomDisplayUnitText,
    bool MajorGridlines,
    bool MinorGridlines,
    int MajorTickMarkIndex,
    int MinorTickMarkIndex,
    int TickLabelPositionIndex,
    int CrossingIndex,
    string CrossesAtText,
    int CrossBetweenIndex,
    int LabelAlignmentIndex,
    string LabelOffsetText,
    int MultiLevelLabelsIndex,
    int AutoCrossingIndex,
    bool ReverseOrder);

public sealed record ChartAxisOptionsDialogInput(
    int AxisIndex,
    string? Title,
    string? TitleFontFamily,
    string? TitleFontSize,
    string? TitleColor,
    bool? TitleBold,
    bool? TitleItalic,
    bool ShowAxis,
    string? Minimum,
    string? Maximum,
    string? MajorUnit,
    string? MinorUnit,
    string? NumberFormatCode,
    int DisplayUnitIndex,
    string? CustomDisplayUnit,
    bool MajorGridlines,
    bool MinorGridlines,
    int MajorTickMarkIndex,
    int MinorTickMarkIndex,
    int TickLabelPositionIndex,
    int CrossingIndex,
    string? CrossesAt,
    int CrossBetweenIndex,
    int LabelAlignmentIndex,
    string? LabelOffset,
    int MultiLevelLabelsIndex,
    int AutoCrossingIndex,
    bool ReverseOrder);

public sealed class ChartAxisOptionsDialogSession
{
    private readonly EditingSession _editor;
    private readonly ChartAxisOptionsPlanner _planner;
    private readonly CultureInfo _culture;
    private ChartAxisOptionsDialogState _state;

    public ChartAxisOptionsDialogSession(
        EditingSession editor,
        ChartAxisKind? initialAxis = null,
        CultureInfo? culture = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _culture = culture ?? CultureInfo.CurrentCulture;
        _planner = ChartAxisOptionsPlanner.FromChart(
            editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected."));
        _planner.SetAxis(initialAxis ?? ChartAxisKind.Value);
        Surface = ChartAxisOptionsPlanner.BuildSurfacePlan();
        _state = BuildState();
    }

    public ChartAxisOptionsSurfacePlan Surface { get; }

    public ChartAxisOptionsDialogState State => _state;

    public IReadOnlyList<string> AxisOptions { get; } =
        ChartAxisOptionsPlanner.AxisOptions.Select(option => option.Label).ToArray();

    public IReadOnlyList<string> DisplayUnitOptions { get; } =
        ChartAxisOptionsPlanner.DisplayUnitOptions.Select(option => option.Label).ToArray();

    public IReadOnlyList<string> TickMarkOptions { get; } =
        ChartAxisOptionsPlanner.TickMarkOptions.Select(option => option.Label).ToArray();

    public IReadOnlyList<string> TickLabelPositionOptions { get; } =
        ChartAxisOptionsPlanner.TickLabelPositionOptions.Select(option => option.Label).ToArray();

    public IReadOnlyList<string> CrossingOptions { get; } =
        ChartAxisOptionsPlanner.CrossingOptions.Select(option => option.Label).ToArray();

    public IReadOnlyList<string> CrossBetweenOptions { get; } =
        ChartAxisOptionsPlanner.CrossBetweenOptions.Select(option => option.Label).ToArray();

    public IReadOnlyList<string> LabelAlignmentOptions { get; } =
        ChartAxisOptionsPlanner.LabelAlignmentOptions.Select(option => option.Label).ToArray();

    public IReadOnlyList<string> MultiLevelLabelsOptions { get; } =
        ChartAxisOptionsPlanner.MultiLevelLabelsOptions.Select(option => option.Label).ToArray();

    public IReadOnlyList<string> AutoCrossingOptions { get; } =
        ChartAxisOptionsPlanner.AutoCrossingOptions.Select(option => option.Label).ToArray();

    public ChartAxisOptionsDialogState SelectAxis(int axisIndex)
    {
        _planner.SetAxis(AxisAt(axisIndex));
        _state = BuildState();
        return _state;
    }

    public bool TryApplySelectionChange(
        ChartOptionsDialogFieldId fieldId,
        int selectedIndex,
        out ChartOptionsDialogPlan plan) =>
        ChartOptionsDialogSelectionTransition.TryApply(
            fieldId,
            ChartOptionsDialogFieldId.Axis,
            selectedIndex,
            SelectAxis,
            this.BuildDialogPlan,
            out plan);

    public ChartAxisOptions BuildCommitPlan(ChartAxisOptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var selectedAxis = AxisAt(input.AxisIndex);
        if (selectedAxis != AxisAt(_state.AxisIndex))
            SelectAxis(input.AxisIndex);

        var baseline = _state;
        var titleFontSize = ParseOptionalDouble(input.TitleFontSize, "Axis title size");
        var minimum = ParseOptionalDouble(input.Minimum, "Minimum");
        var maximum = ParseOptionalDouble(input.Maximum, "Maximum");
        var majorUnit = ParseOptionalDouble(input.MajorUnit, "Major unit");
        var minorUnit = ParseOptionalDouble(input.MinorUnit, "Minor unit");
        var customDisplayUnit = ParseOptionalDouble(input.CustomDisplayUnit, "Custom display-unit divisor");
        var crossesAt = ParseOptionalDouble(input.CrossesAt, "Crosses at");
        var labelOffset = ChartDialogOptionProjection.ParseOptionalInt(
            input.LabelOffset,
            _culture,
            value => value is >= 0 and <= 100,
            "Label offset must be an integer from 0 to 100 or blank.");

        if (!string.Equals(input.TitleColor, baseline.TitleColor, StringComparison.Ordinal))
            _planner.SetTitleColor(input.TitleColor);
        _planner.SetTitle(input.Title);
        _planner.SetTitleFontFamily(input.TitleFontFamily);
        _planner.SetTitleFontSizePt(titleFontSize);
        _planner.SetTitleBold(input.TitleBold);
        _planner.SetTitleItalic(input.TitleItalic);
        _planner.SetShowAxis(input.ShowAxis);
        _planner.SetMinimum(minimum);
        _planner.SetMaximum(maximum);
        _planner.SetMajorUnit(majorUnit);
        _planner.SetMinorUnit(minorUnit);
        _planner.SetNumberFormatCode(input.NumberFormatCode);
        SetWhenChanged(
            input.DisplayUnitIndex,
            baseline.DisplayUnitIndex,
            () => _planner.SetDisplayUnit(ValueAt(
                ChartAxisOptionsPlanner.DisplayUnitOptions,
                input.DisplayUnitIndex,
                option => option.Value,
                ChartAxisDisplayUnit.None)));
        if (customDisplayUnit != ParseOptionalDouble(baseline.CustomDisplayUnitText, "Custom display-unit divisor"))
            _planner.SetCustomDisplayUnit(customDisplayUnit);
        _planner.SetMajorGridlines(input.MajorGridlines);
        _planner.SetMinorGridlines(input.MinorGridlines);
        SetWhenChanged(
            input.MajorTickMarkIndex,
            baseline.MajorTickMarkIndex,
            () => _planner.SetMajorTickMark(ValueAt(
                ChartAxisOptionsPlanner.TickMarkOptions,
                input.MajorTickMarkIndex,
                option => option.Value,
                default(ChartTickMark?))));
        SetWhenChanged(
            input.MinorTickMarkIndex,
            baseline.MinorTickMarkIndex,
            () => _planner.SetMinorTickMark(ValueAt(
                ChartAxisOptionsPlanner.TickMarkOptions,
                input.MinorTickMarkIndex,
                option => option.Value,
                default(ChartTickMark?))));
        SetWhenChanged(
            input.TickLabelPositionIndex,
            baseline.TickLabelPositionIndex,
            () => _planner.SetTickLabelPosition(ValueAt(
                ChartAxisOptionsPlanner.TickLabelPositionOptions,
                input.TickLabelPositionIndex,
                option => option.Value,
                default(ChartTickLabelPosition?))));
        SetWhenChanged(
            input.CrossingIndex,
            baseline.CrossingIndex,
            () => _planner.SetCrosses(ValueAt(
                ChartAxisOptionsPlanner.CrossingOptions,
                input.CrossingIndex,
                option => option.Value,
                default(ChartAxisCrossing?))));
        if (crossesAt != ParseOptionalDouble(baseline.CrossesAtText, "Crosses at"))
            _planner.SetCrossesAt(crossesAt);
        SetWhenChanged(
            input.CrossBetweenIndex,
            baseline.CrossBetweenIndex,
            () => _planner.SetCrossBetween(ValueAt(
                ChartAxisOptionsPlanner.CrossBetweenOptions,
                input.CrossBetweenIndex,
                option => option.Value,
                default(ChartCrossBetween?))));
        SetWhenChanged(
            input.LabelAlignmentIndex,
            baseline.LabelAlignmentIndex,
            () => _planner.SetLabelAlignment(ValueAt(
                ChartAxisOptionsPlanner.LabelAlignmentOptions,
                input.LabelAlignmentIndex,
                option => option.Value,
                default(ChartLabelAlignment?))));
        _planner.SetLabelOffsetPercent(labelOffset);
        SetWhenChanged(
            input.MultiLevelLabelsIndex,
            baseline.MultiLevelLabelsIndex,
            () => _planner.SetNoMultiLevelLabels(ValueAt(
                ChartAxisOptionsPlanner.MultiLevelLabelsOptions,
                input.MultiLevelLabelsIndex,
                option => option.Value,
                default(bool?))));
        SetWhenChanged(
            input.AutoCrossingIndex,
            baseline.AutoCrossingIndex,
            () => _planner.SetAutoCrossing(ValueAt(
                ChartAxisOptionsPlanner.AutoCrossingOptions,
                input.AutoCrossingIndex,
                option => option.Value,
                default(bool?))));
        _planner.SetReverseOrder(input.ReverseOrder);

        var plan = _planner.BuildCommitPlan();
        _state = BuildState();
        return plan;
    }

    public ChartDialogSessionResult<ChartAxisOptions> Submit(ChartAxisOptionsDialogInput input)
    {
        try
        {
            var plan = BuildCommitPlan(input);
            _editor.ApplyChartAxisOptions(plan);
            return ChartDialogSessionResult<ChartAxisOptions>.Accepted(plan);
        }
        catch (FormatException ex)
        {
            return ChartDialogSessionResult<ChartAxisOptions>.Invalid(ex.Message);
        }
    }

    public string Format(double? value) => ChartDialogOptionProjection.Format(value, _culture);

    public string Format(int? value) => ChartDialogOptionProjection.Format(value, _culture);

    public int FindTickMarkIndex(ChartTickMark? value) => FindIndex(
        ChartAxisOptionsPlanner.TickMarkOptions,
        value,
        option => option.Value);

    public int FindTickLabelPositionIndex(ChartTickLabelPosition? value) => FindIndex(
        ChartAxisOptionsPlanner.TickLabelPositionOptions,
        value,
        option => option.Value);

    public int FindCrossingIndex(ChartAxisCrossing? value) => FindIndex(
        ChartAxisOptionsPlanner.CrossingOptions,
        value,
        option => option.Value);

    public int FindCrossBetweenIndex(ChartCrossBetween? value) => FindIndex(
        ChartAxisOptionsPlanner.CrossBetweenOptions,
        value,
        option => option.Value);

    public int FindLabelAlignmentIndex(ChartLabelAlignment? value) => FindIndex(
        ChartAxisOptionsPlanner.LabelAlignmentOptions,
        value,
        option => option.Value);

    public int FindMultiLevelLabelsIndex(bool? value) => FindIndex(
        ChartAxisOptionsPlanner.MultiLevelLabelsOptions,
        value,
        option => option.Value);

    public int FindAutoCrossingIndex(bool? value) => FindIndex(
        ChartAxisOptionsPlanner.AutoCrossingOptions,
        value,
        option => option.Value);

    private ChartAxisOptionsDialogState BuildState() => new(
        FindIndex(ChartAxisOptionsPlanner.AxisOptions, _planner.Axis, option => option.Value),
        _planner.Title,
        _planner.TitleFontFamily ?? string.Empty,
        Format(_planner.TitleFontSizePt),
        _planner.TitleColorText,
        _planner.TitleBold,
        _planner.TitleItalic,
        _planner.ShowAxis,
        Format(_planner.Minimum),
        Format(_planner.Maximum),
        Format(_planner.MajorUnit),
        Format(_planner.MinorUnit),
        _planner.NumberFormatCode,
        FindIndex(ChartAxisOptionsPlanner.DisplayUnitOptions, _planner.DisplayUnit, option => option.Value),
        Format(_planner.CustomDisplayUnit),
        _planner.MajorGridlines,
        _planner.MinorGridlines,
        FindTickMarkIndex(_planner.MajorTickMark),
        FindTickMarkIndex(_planner.MinorTickMark),
        FindTickLabelPositionIndex(_planner.TickLabelPosition),
        FindCrossingIndex(_planner.Crosses),
        Format(_planner.CrossesAt),
        FindCrossBetweenIndex(_planner.CrossBetween),
        FindLabelAlignmentIndex(_planner.LabelAlignment),
        Format(_planner.LabelOffsetPercent),
        FindMultiLevelLabelsIndex(_planner.NoMultiLevelLabels),
        FindAutoCrossingIndex(_planner.AutoCrossing),
        _planner.ReverseOrder);

    private double? ParseOptionalDouble(string? text, string label) =>
        ChartDialogOptionProjection.ParseOptionalDouble(
            text,
            _culture,
            double.IsFinite,
            $"{label} must be a finite number or blank.");

    private static ChartAxisKind AxisAt(int selectedIndex) => ValueAt(
        ChartAxisOptionsPlanner.AxisOptions,
        selectedIndex,
        option => option.Value,
        ChartAxisKind.Value);

    private static int FindIndex<TOption, TValue>(
        IReadOnlyList<TOption> options,
        TValue value,
        Func<TOption, TValue> selector) =>
        ChartDialogOptionProjection.FindIndex(options, value, selector);

    private static TValue ValueAt<TOption, TValue>(
        IReadOnlyList<TOption> options,
        int index,
        Func<TOption, TValue> selector,
        TValue fallback) =>
        ChartDialogOptionProjection.ValueAtOrDefault(options, index, selector, fallback);

    private static void SetWhenChanged(int selectedIndex, int baselineIndex, Action setter)
    {
        if (selectedIndex != baselineIndex)
            setter();
    }
}

public sealed record ChartDisplayOptionsDialogState(
    string Title,
    bool TitleOverlay,
    int TitlePositionIndex,
    int TitleAlignmentIndex,
    bool SupportsChartExTitleLayout,
    bool PlotVisibleOnly,
    bool RoundedCorners,
    int StyleIndex,
    int LegendIndex,
    bool ShowValueLabels,
    bool ShowPercentLabels,
    bool ShowCategoryLabels,
    bool ShowSeriesLabels,
    bool ShowLegendKeys,
    bool ShowBubbleSize,
    bool? ShowLeaderLines,
    bool SupportsLeaderLines,
    string LabelNumberFormat,
    string LabelSeparator,
    string LabelFontFamily,
    string LabelFontSizeText,
    bool? LabelBold,
    bool? LabelItalic,
    string LabelColor,
    int LabelPositionIndex,
    bool CategoryGridlines,
    bool ValueGridlines,
    string BarGapWidthText,
    string BarOverlapText,
    int DisplayBlanksIndex,
    bool? ShowDataLabelsOverMaximum,
    bool VaryColors,
    bool? LegendOverlay,
    bool? HighLowLines,
    bool SupportsHighLowLines,
    bool? WaterfallConnectorLines,
    bool SupportsWaterfallConnectorLines,
    bool? DropLines,
    bool SupportsDropLines,
    bool? UpDownBars,
    bool SupportsUpDownBars,
    bool? SeriesLines,
    bool SupportsSeriesLines);

public sealed record ChartDisplayOptionsDialogInput(
    string? Title,
    bool TitleOverlay,
    int TitlePositionIndex,
    int TitleAlignmentIndex,
    bool PlotVisibleOnly,
    bool RoundedCorners,
    int StyleIndex,
    int LegendIndex,
    bool ShowValueLabels,
    bool ShowPercentLabels,
    bool ShowCategoryLabels,
    bool ShowSeriesLabels,
    bool ShowLegendKeys,
    bool ShowBubbleSize,
    bool? ShowLeaderLines,
    string? LabelNumberFormat,
    string? LabelSeparator,
    string? LabelFontFamily,
    string? LabelFontSize,
    bool? LabelBold,
    bool? LabelItalic,
    string? LabelColor,
    int LabelPositionIndex,
    bool CategoryGridlines,
    bool ValueGridlines,
    string? BarGapWidth,
    string? BarOverlap,
    int DisplayBlanksIndex,
    bool? ShowDataLabelsOverMaximum,
    bool VaryColors,
    bool? LegendOverlay,
    bool? HighLowLines,
    bool? WaterfallConnectorLines,
    bool? DropLines,
    bool? UpDownBars,
    bool? SeriesLines);

public sealed class ChartDisplayOptionsDialogSession
{
    private readonly EditingSession _editor;
    private readonly ChartDisplayOptionsPlanner _planner;
    private readonly CultureInfo _culture;
    private ChartDisplayOptionsDialogState _state;

    public ChartDisplayOptionsDialogSession(
        EditingSession editor,
        CultureInfo? culture = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _culture = culture ?? CultureInfo.CurrentCulture;
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartDisplayOptionsPlanner.FromChart(chart);
        SupportsLeaderLines = chart.ChartType is ChartType.Pie or ChartType.Doughnut;
        Surface = ChartDisplayOptionsPlanner.BuildSurfacePlan();
        StyleOptions = _planner.AvailableStyleOptions.Select(option => option.Label).ToArray();
        _state = BuildState();
    }

    public ChartDisplayOptionsSurfacePlan Surface { get; }

    public ChartDisplayOptionsDialogState State => _state;

    public IReadOnlyList<string> StyleOptions { get; }

    public IReadOnlyList<string> LegendOptions { get; } =
        ChartDisplayOptionsPlanner.LegendOptions.Select(option => option.Label).ToArray();

    public IReadOnlyList<string> TitlePositionOptions { get; } =
        ChartDisplayOptionsPlanner.TitlePositionOptions.Select(option => option.Label).ToArray();

    public IReadOnlyList<string> TitleAlignmentOptions { get; } =
        ChartDisplayOptionsPlanner.TitleAlignmentOptions.Select(option => option.Label).ToArray();

    public IReadOnlyList<string> LabelPositionOptions { get; } =
        ChartDisplayOptionsPlanner.LabelPositionOptions.Select(option => option.Label).ToArray();

    public IReadOnlyList<string> DisplayBlanksOptions { get; } =
        ChartDisplayOptionsPlanner.DisplayBlanksOptions.Select(option => option.Label).ToArray();

    private bool SupportsLeaderLines { get; }

    public ChartDisplayOptions BuildCommitPlan(ChartDisplayOptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var labelFontSize = ChartDialogOptionProjection.ParseOptionalDouble(
            input.LabelFontSize,
            _culture,
            value => double.IsFinite(value) && value >= 0,
            "Label font size must be a non-negative finite number or blank.");
        var barGapWidth = ParseOptionalPercent(input.BarGapWidth, "Bar gap width", 0, 500);
        var barOverlap = ParseOptionalPercent(input.BarOverlap, "Bar overlap", -100, 100);

        if (!string.Equals(input.LabelColor, _state.LabelColor, StringComparison.Ordinal))
            _planner.SetLabelColor(input.LabelColor);
        _planner.SetTitle(input.Title);
        if (input.TitleOverlay != _state.TitleOverlay)
            _planner.SetTitleOverlay(input.TitleOverlay);
        if (_planner.SupportsChartExTitleLayout)
        {
            if (input.TitlePositionIndex != _state.TitlePositionIndex)
            {
                _planner.SetTitlePosition(ValueAt(
                    ChartDisplayOptionsPlanner.TitlePositionOptions,
                    input.TitlePositionIndex,
                    option => option.Value,
                    _planner.TitlePosition));
            }

            if (input.TitleAlignmentIndex != _state.TitleAlignmentIndex)
            {
                _planner.SetTitleAlignment(ValueAt(
                    ChartDisplayOptionsPlanner.TitleAlignmentOptions,
                    input.TitleAlignmentIndex,
                    option => option.Value,
                    _planner.TitleAlignment));
            }
        }
        if (input.PlotVisibleOnly != _state.PlotVisibleOnly)
            _planner.SetPlotVisibleOnly(input.PlotVisibleOnly);
        if (input.RoundedCorners != _state.RoundedCorners)
            _planner.SetRoundedCorners(input.RoundedCorners);
        _planner.SetStyleId(ValueAt(
            _planner.AvailableStyleOptions,
            input.StyleIndex,
            option => option.Value,
            default(int?)));
        _planner.SetLegend(ValueAt(
            ChartDisplayOptionsPlanner.LegendOptions,
            input.LegendIndex,
            option => option.Value,
            default(LegendPosition?)));
        _planner.SetShowValueLabels(input.ShowValueLabels);
        _planner.SetShowPercentLabels(input.ShowPercentLabels);
        _planner.SetShowCategoryLabels(input.ShowCategoryLabels);
        _planner.SetShowSeriesLabels(input.ShowSeriesLabels);
        _planner.SetShowLegendKeys(input.ShowLegendKeys);
        _planner.SetShowBubbleSize(input.ShowBubbleSize);
        _planner.SetShowLeaderLines(input.ShowLeaderLines);
        _planner.SetLabelPosition(ValueAt(
            ChartDisplayOptionsPlanner.LabelPositionOptions,
            input.LabelPositionIndex,
            option => option.Value,
            DataLabelPosition.BestFit));
        _planner.SetLabelNumberFormat(input.LabelNumberFormat);
        _planner.SetLabelSeparator(input.LabelSeparator);
        _planner.SetLabelFontFamily(input.LabelFontFamily);
        _planner.SetLabelFontSize(labelFontSize);
        _planner.SetLabelBold(input.LabelBold);
        _planner.SetLabelItalic(input.LabelItalic);
        _planner.SetCategoryGridlines(input.CategoryGridlines);
        _planner.SetValueGridlines(input.ValueGridlines);
        _planner.SetBarGapWidthPercent(barGapWidth);
        _planner.SetBarOverlapPercent(barOverlap);
        _planner.SetDisplayBlanksAs(ValueAt(
            ChartDisplayOptionsPlanner.DisplayBlanksOptions,
            input.DisplayBlanksIndex,
            option => option.Value,
            default(ChartDisplayBlanksAs?)));
        _planner.SetShowDataLabelsOverMaximum(input.ShowDataLabelsOverMaximum);
        _planner.SetVaryColors(input.VaryColors);
        _planner.SetLegendOverlay(input.LegendOverlay);
        _planner.SetHighLowLines(input.HighLowLines);
        _planner.SetWaterfallConnectorLines(input.WaterfallConnectorLines);
        _planner.SetDropLines(input.DropLines);
        _planner.SetUpDownBars(input.UpDownBars);
        _planner.SetSeriesLines(input.SeriesLines);

        var plan = _planner.BuildCommitPlan();
        _state = BuildState();
        return plan;
    }

    public ChartDialogSessionResult<ChartDisplayOptions> Submit(ChartDisplayOptionsDialogInput input)
    {
        try
        {
            var plan = BuildCommitPlan(input);
            _editor.ApplyChartDisplayOptions(plan);
            return ChartDialogSessionResult<ChartDisplayOptions>.Accepted(plan);
        }
        catch (FormatException ex)
        {
            return ChartDialogSessionResult<ChartDisplayOptions>.Invalid(ex.Message);
        }
    }

    public string Format(double? value) => ChartDialogOptionProjection.Format(value, _culture);

    public string Format(int? value) => ChartDialogOptionProjection.Format(value, _culture);

    public int FindStyleIndex(int? value) => FindIndex(
        _planner.AvailableStyleOptions,
        value,
        option => option.Value);

    public int FindLegendIndex(LegendPosition? value) => FindIndex(
        ChartDisplayOptionsPlanner.LegendOptions,
        value,
        option => option.Value);

    public int FindTitlePositionIndex(ChartExTitlePosition value) => FindIndex(
        ChartDisplayOptionsPlanner.TitlePositionOptions,
        value,
        option => option.Value);

    public int FindTitleAlignmentIndex(ChartExTitleAlignment value) => FindIndex(
        ChartDisplayOptionsPlanner.TitleAlignmentOptions,
        value,
        option => option.Value);

    public int FindLabelPositionIndex(DataLabelPosition value) => FindIndex(
        ChartDisplayOptionsPlanner.LabelPositionOptions,
        value,
        option => option.Value);

    public int FindDisplayBlanksIndex(ChartDisplayBlanksAs? value) => FindIndex(
        ChartDisplayOptionsPlanner.DisplayBlanksOptions,
        value,
        option => option.Value);

    private ChartDisplayOptionsDialogState BuildState() => new(
        _planner.Title,
        _planner.TitleOverlay,
        FindTitlePositionIndex(_planner.TitlePosition),
        FindTitleAlignmentIndex(_planner.TitleAlignment),
        _planner.SupportsChartExTitleLayout,
        _planner.PlotVisibleOnly,
        _planner.RoundedCorners,
        FindStyleIndex(_planner.StyleId),
        FindLegendIndex(_planner.Legend),
        _planner.ShowValueLabels,
        _planner.ShowPercentLabels,
        _planner.ShowCategoryLabels,
        _planner.ShowSeriesLabels,
        _planner.ShowLegendKeys,
        _planner.ShowBubbleSize,
        _planner.ShowLeaderLines,
        SupportsLeaderLines,
        _planner.LabelNumberFormat,
        _planner.LabelSeparator,
        _planner.LabelFontFamily,
        Format(_planner.LabelFontSizePt),
        _planner.LabelBold,
        _planner.LabelItalic,
        _planner.LabelColorText,
        FindLabelPositionIndex(_planner.LabelPosition),
        _planner.CategoryGridlines,
        _planner.ValueGridlines,
        Format(_planner.BarGapWidthPercent),
        Format(_planner.BarOverlapPercent),
        FindDisplayBlanksIndex(_planner.DisplayBlanksAs),
        _planner.ShowDataLabelsOverMaximum,
        _planner.VaryColors,
        _planner.LegendOverlay,
        _planner.HighLowLines,
        _planner.SupportsHighLowLines,
        _planner.WaterfallConnectorLines,
        _planner.SupportsWaterfallConnectorLines,
        _planner.DropLines,
        _planner.SupportsDropLines,
        _planner.UpDownBars,
        _planner.SupportsUpDownBars,
        _planner.SeriesLines,
        _planner.SupportsSeriesLines);

    private int? ParseOptionalPercent(string? text, string surface, int minimum, int maximum) =>
        ChartDialogOptionProjection.ParseOptionalInt(
            text,
            _culture,
            value => value >= minimum && value <= maximum,
            $"{surface} must be a whole number from {minimum} to {maximum}, or blank.");

    private static int FindIndex<TOption, TValue>(
        IReadOnlyList<TOption> options,
        TValue value,
        Func<TOption, TValue> selector) =>
        ChartDialogOptionProjection.FindIndex(options, value, selector);

    private static TValue ValueAt<TOption, TValue>(
        IReadOnlyList<TOption> options,
        int index,
        Func<TOption, TValue> selector,
        TValue fallback) =>
        ChartDialogOptionProjection.ValueAtOrDefault(options, index, selector, fallback);
}
