using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartPointOptionsDialogState(
    IReadOnlyList<ChartSeriesOption> SeriesOptions,
    int SeriesIndex,
    IReadOnlyList<ChartPointOption> PointOptions,
    int PointIndex,
    string FillColorText,
    string StrokeColorText,
    string StrokeWidthText,
    bool UsePointDataLabels,
    bool ShowValueLabels,
    bool ShowPercentLabels,
    bool ShowCategoryLabels,
    bool ShowSeriesLabels,
    bool ShowLegendKeys,
    bool ShowBubbleSize,
    bool? ShowLeaderLines,
    int LabelPositionIndex,
    string LabelNumberFormat,
    string LabelSeparator,
    string LabelFontFamily,
    string LabelFontSizeText,
    bool? LabelBold,
    bool? LabelItalic,
    string LabelColorText,
    int MarkerIndex,
    string MarkerSizeText,
    string ExplosionText);

public sealed record ChartPointOptionsDialogInput(
    int SeriesIndex,
    int PointIndex,
    string? FillColorText,
    string? StrokeColorText,
    string? StrokeWidthText,
    bool UsePointDataLabels,
    bool ShowValueLabels,
    bool ShowPercentLabels,
    bool ShowCategoryLabels,
    bool ShowSeriesLabels,
    bool ShowLegendKeys,
    bool ShowBubbleSize,
    bool? ShowLeaderLines,
    int LabelPositionIndex,
    string? LabelNumberFormat,
    string? LabelSeparator,
    string? LabelFontFamily,
    string? LabelFontSizeText,
    bool? LabelBold,
    bool? LabelItalic,
    string? LabelColorText,
    int MarkerIndex,
    string? MarkerSizeText,
    string? ExplosionText);

public sealed class ChartPointOptionsDialogSession
{
    private readonly EditingSession _editor;
    private readonly ChartPointOptionsPlanner _planner;
    private readonly CultureInfo _culture;

    public ChartPointOptionsDialogSession(
        EditingSession editor,
        int? initialSeriesIndex = null,
        int? initialPointIndex = null,
        CultureInfo? culture = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _culture = culture ?? CultureInfo.CurrentCulture;
        _planner = ChartPointOptionsPlanner.FromChart(
            editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected."));
        if (initialSeriesIndex is { } seriesIndex)
            _planner.SetSeriesIndex(seriesIndex);
        if (initialPointIndex is { } pointIndex)
            _planner.SetPointIndex(pointIndex);
        Surface = ChartPointOptionsPlanner.BuildSurfacePlan();
    }

    public ChartPointOptionsSurfacePlan Surface { get; }

    public ChartPointOptionsDialogState State => BuildState();

    public IReadOnlyList<ChartDisplayLabelPositionOption> LabelPositionOptions =>
        ChartDisplayOptionsPlanner.LabelPositionOptions;

    public IReadOnlyList<ChartMarkerSymbolOption> MarkerOptions =>
        ChartPointOptionsPlanner.MarkerOptions;

    public ChartPointOptionsDialogState SelectSeries(int seriesIndex)
    {
        _planner.SetSeriesIndex(seriesIndex);
        return BuildState();
    }

    public ChartPointOptionsDialogState SelectPoint(int pointIndex)
    {
        _planner.SetPointIndex(pointIndex);
        return BuildState();
    }

    public string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, _culture);

    public string Format(int? value) =>
        ChartDialogOptionProjection.Format(value, _culture);

    public int FindLabelPositionIndex(DataLabelPosition value) =>
        ChartDialogOptionProjection.FindIndex(
            LabelPositionOptions,
            value,
            option => option.Value);

    public int FindMarkerIndex(ChartMarkerSymbol? value) =>
        ChartDialogOptionProjection.FindIndex(
            MarkerOptions,
            value ?? ChartMarkerSymbol.Auto,
            option => option.Value);

    public ChartPointOptions BuildCommitPlan(ChartPointOptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        _planner.SetSeriesIndex(input.SeriesIndex);
        _planner.SetPointIndex(input.PointIndex);
        _planner.SetFillColor(input.FillColorText);
        _planner.SetStrokeColor(input.StrokeColorText);
        _planner.SetStrokeWidth(ParseOptional(input.StrokeWidthText, "Outline width"));
        _planner.SetUsePointDataLabels(input.UsePointDataLabels);
        _planner.SetShowValueLabels(input.ShowValueLabels);
        _planner.SetShowPercentLabels(input.ShowPercentLabels);
        _planner.SetShowCategoryLabels(input.ShowCategoryLabels);
        _planner.SetShowSeriesLabels(input.ShowSeriesLabels);
        _planner.SetShowLegendKeys(input.ShowLegendKeys);
        _planner.SetShowBubbleSize(input.ShowBubbleSize);
        _planner.SetShowLeaderLines(input.ShowLeaderLines);
        _planner.SetLabelPosition(ChartDialogOptionProjection.ValueAtOrDefault(
            LabelPositionOptions,
            input.LabelPositionIndex,
            option => option.Value,
            _planner.LabelPosition));
        _planner.SetLabelNumberFormat(input.LabelNumberFormat);
        _planner.SetLabelSeparator(input.LabelSeparator);
        _planner.SetLabelFontFamily(input.LabelFontFamily);
        _planner.SetLabelFontSize(ParseOptional(input.LabelFontSizeText, "Label font size"));
        _planner.SetLabelBold(input.LabelBold);
        _planner.SetLabelItalic(input.LabelItalic);
        _planner.SetLabelColor(input.LabelColorText);
        var marker = ChartDialogOptionProjection.ValueAtOrDefault(
            MarkerOptions,
            input.MarkerIndex,
            option => option.Value,
            ChartMarkerSymbol.Auto);
        _planner.SetMarkerSymbol(marker == ChartMarkerSymbol.Auto ? null : marker);
        _planner.SetMarkerSize(ParseOptional(input.MarkerSizeText, "Marker size"));
        _planner.SetExplosionPercent(ParseExplosion(input.ExplosionText));
        return _planner.BuildCommitPlan();
    }

    public ChartOptionsDialogCommitResult<ChartPointOptions> TryCommit(
        ChartPointOptionsDialogInput input)
    {
        try
        {
            var plan = BuildCommitPlan(input);
            _editor.ApplyChartPointOptions(plan);
            return ChartOptionsDialogCommitResult<ChartPointOptions>.Applied(plan);
        }
        catch (FormatException ex)
        {
            return ChartOptionsDialogCommitResult<ChartPointOptions>.Invalid(ex.Message);
        }
    }

    private ChartPointOptionsDialogState BuildState() => new(
        _planner.SeriesOptions,
        _planner.SeriesIndex,
        _planner.PointOptions,
        _planner.PointIndex,
        _planner.FillColorText,
        _planner.StrokeColorText,
        Format(_planner.StrokeWidthPt),
        _planner.UsePointDataLabels,
        _planner.ShowValueLabels,
        _planner.ShowPercentLabels,
        _planner.ShowCategoryLabels,
        _planner.ShowSeriesLabels,
        _planner.ShowLegendKeys,
        _planner.ShowBubbleSize,
        _planner.ShowLeaderLines,
        FindLabelPositionIndex(_planner.LabelPosition),
        _planner.LabelNumberFormat,
        _planner.LabelSeparator,
        _planner.LabelFontFamily,
        Format(_planner.LabelFontSizePt),
        _planner.LabelBold,
        _planner.LabelItalic,
        _planner.LabelColorText,
        FindMarkerIndex(_planner.MarkerSymbol),
        Format(_planner.MarkerSizePt),
        Format(_planner.ExplosionPercent));

    private double? ParseOptional(string? text, string label) =>
        ChartDialogOptionProjection.ParseOptionalDouble(
            text,
            _culture,
            value => double.IsFinite(value) && value >= 0,
            $"{label} must be a non-negative finite number or blank.");

    private int? ParseExplosion(string? text) =>
        ChartDialogOptionProjection.ParseOptionalInt(
            text,
            _culture,
            value => value is >= 0 and <= 100,
            "Explosion must be an integer from 0 to 100 or blank.");
}

public sealed record ChartSeriesOptionsDialogState(
    IReadOnlyList<ChartSeriesOption> SeriesOptions,
    int SeriesIndex,
    IReadOnlyList<ChartSeriesChartTypeOption> SeriesChartTypeOptions,
    int SeriesChartTypeIndex,
    bool SmoothLine,
    bool OnSecondaryAxis,
    bool? InvertIfNegative,
    string LineWidthText,
    string LineColorText,
    int LineDashIndex,
    bool NoLine,
    string FillColorText,
    bool UseSeriesDataLabels,
    bool ShowValueLabels,
    bool ShowPercentLabels,
    bool ShowCategoryLabels,
    bool ShowSeriesLabels,
    bool ShowLegendKeys,
    bool ShowBubbleSize,
    bool? ShowLeaderLines,
    bool ErrorBarsEnabled,
    int ErrorDirectionIndex,
    int ErrorBarTypeIndex,
    int ErrorValueTypeIndex,
    string ErrorValueText,
    bool ErrorNoEndCap,
    bool TrendlineEnabled,
    int TrendlineTypeIndex,
    string TrendlineOrderText,
    string TrendlinePeriodText,
    string TrendlineForwardText,
    string TrendlineBackwardText,
    bool TrendlineEquation,
    bool TrendlineRSquared,
    int LabelPositionIndex,
    string LabelNumberFormat,
    string LabelSeparator,
    string LabelFontFamily,
    string LabelFontSizeText,
    bool? LabelBold,
    bool? LabelItalic,
    string LabelColorText,
    int MarkerIndex,
    string MarkerSizeText);

public sealed record ChartSeriesOptionsDialogInput(
    int SeriesIndex,
    int SeriesChartTypeIndex,
    bool SmoothLine,
    bool OnSecondaryAxis,
    bool? InvertIfNegative,
    string? LineWidthText,
    string? LineColorText,
    int LineDashIndex,
    bool NoLine,
    string? FillColorText,
    bool UseSeriesDataLabels,
    bool ShowValueLabels,
    bool ShowPercentLabels,
    bool ShowCategoryLabels,
    bool ShowSeriesLabels,
    bool ShowLegendKeys,
    bool ShowBubbleSize,
    bool? ShowLeaderLines,
    bool ErrorBarsEnabled,
    int ErrorDirectionIndex,
    int ErrorBarTypeIndex,
    int ErrorValueTypeIndex,
    string? ErrorValueText,
    bool ErrorNoEndCap,
    bool TrendlineEnabled,
    int TrendlineTypeIndex,
    string? TrendlineOrderText,
    string? TrendlinePeriodText,
    string? TrendlineForwardText,
    string? TrendlineBackwardText,
    bool TrendlineEquation,
    bool TrendlineRSquared,
    int LabelPositionIndex,
    string? LabelNumberFormat,
    string? LabelSeparator,
    string? LabelFontFamily,
    string? LabelFontSizeText,
    bool? LabelBold,
    bool? LabelItalic,
    string? LabelColorText,
    int MarkerIndex,
    string? MarkerSizeText);

public sealed class ChartSeriesOptionsDialogSession
{
    private readonly EditingSession _editor;
    private readonly ChartSeriesOptionsPlanner _planner;
    private readonly CultureInfo _culture;

    public ChartSeriesOptionsDialogSession(
        EditingSession editor,
        int? initialSeriesIndex = null,
        CultureInfo? culture = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _culture = culture ?? CultureInfo.CurrentCulture;
        _planner = ChartSeriesOptionsPlanner.FromChart(
            editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected."));
        _planner.SetSeriesIndex(initialSeriesIndex ?? _planner.SeriesIndex);
        Surface = ChartSeriesOptionsPlanner.BuildSurfacePlan();
    }

    public ChartSeriesOptionsSurfacePlan Surface { get; }

    public ChartSeriesOptionsDialogState State => BuildState();

    public IReadOnlyList<ChartDashOption> DashOptions =>
        ChartSeriesOptionsPlanner.DashOptions;

    public IReadOnlyList<ChartErrorDirectionOption> ErrorDirectionOptions =>
        ChartSeriesOptionsPlanner.ErrorDirectionOptions;

    public IReadOnlyList<ChartErrorBarTypeOption> ErrorBarTypeOptions =>
        ChartSeriesOptionsPlanner.ErrorBarTypeOptions;

    public IReadOnlyList<ChartErrorValueTypeOption> ErrorValueTypeOptions =>
        ChartSeriesOptionsPlanner.ErrorValueTypeOptions;

    public IReadOnlyList<ChartTrendlineTypeOption> TrendlineTypeOptions =>
        ChartSeriesOptionsPlanner.TrendlineTypeOptions;

    public IReadOnlyList<ChartDisplayLabelPositionOption> LabelPositionOptions =>
        ChartDisplayOptionsPlanner.LabelPositionOptions;

    public IReadOnlyList<ChartMarkerSymbolOption> MarkerOptions =>
        ChartSeriesOptionsPlanner.MarkerOptions;

    public string LeaderLinesLabel => ChartSeriesOptionsPlanner.LeaderLinesLabel;
    public string ErrorBarsLabel => ChartSeriesOptionsPlanner.ErrorBarsLabel;
    public string ErrorDirectionLabel => ChartSeriesOptionsPlanner.ErrorDirectionLabel;
    public string ErrorBarTypeLabel => ChartSeriesOptionsPlanner.ErrorBarTypeLabel;
    public string ErrorValueTypeLabel => ChartSeriesOptionsPlanner.ErrorValueTypeLabel;
    public string ErrorValueLabel => ChartSeriesOptionsPlanner.ErrorValueLabel;
    public string ErrorNoEndCapLabel => ChartSeriesOptionsPlanner.ErrorNoEndCapLabel;
    public string TrendlineLabel => ChartSeriesOptionsPlanner.TrendlineLabel;
    public string TrendlineTypeLabel => ChartSeriesOptionsPlanner.TrendlineTypeLabel;
    public string TrendlineOrderLabel => ChartSeriesOptionsPlanner.TrendlineOrderLabel;
    public string TrendlinePeriodLabel => ChartSeriesOptionsPlanner.TrendlinePeriodLabel;
    public string TrendlineForwardLabel => ChartSeriesOptionsPlanner.TrendlineForwardLabel;
    public string TrendlineBackwardLabel => ChartSeriesOptionsPlanner.TrendlineBackwardLabel;
    public string TrendlineEquationLabel => ChartSeriesOptionsPlanner.TrendlineEquationLabel;
    public string TrendlineRSquaredLabel => ChartSeriesOptionsPlanner.TrendlineRSquaredLabel;

    public ChartSeriesOptionsDialogState SelectSeries(int seriesIndex)
    {
        _planner.SetSeriesIndex(seriesIndex);
        return BuildState();
    }

    public string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, _culture);

    public string Format(int? value) =>
        ChartDialogOptionProjection.Format(value, _culture);

    public int FindSeriesChartTypeIndex(ChartType? value)
    {
        var options = BuildSeriesChartTypeOptions(value);
        return ChartDialogOptionProjection.FindIndex(options, value, option => option.Value);
    }

    public int FindDashIndex(OutlineDash value) =>
        FindIndex(DashOptions, value, option => option.Value);

    public int FindErrorDirectionIndex(ChartErrorDirection value) =>
        FindIndex(ErrorDirectionOptions, value, option => option.Value);

    public int FindErrorBarTypeIndex(ChartErrorBarType value) =>
        FindIndex(ErrorBarTypeOptions, value, option => option.Value);

    public int FindErrorValueTypeIndex(ChartErrorValueType value) =>
        FindIndex(ErrorValueTypeOptions, value, option => option.Value);

    public int FindTrendlineTypeIndex(ChartTrendlineType value) =>
        FindIndex(TrendlineTypeOptions, value, option => option.Value);

    public int FindLabelPositionIndex(DataLabelPosition value) =>
        FindIndex(LabelPositionOptions, value, option => option.Value);

    public int FindMarkerIndex(ChartMarkerSymbol value) =>
        FindIndex(MarkerOptions, value, option => option.Value);

    public ChartSeriesOptions BuildCommitPlan(ChartSeriesOptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        _planner.SetSeriesIndex(input.SeriesIndex);
        var seriesChartTypeOptions = BuildSeriesChartTypeOptions(_planner.OverrideChartType);
        var overrideChartType = ChartDialogOptionProjection.ValueAtOrDefault(
            seriesChartTypeOptions,
            input.SeriesChartTypeIndex,
            option => option.Value,
            _planner.OverrideChartType);
        if (overrideChartType is null or ChartType.Line or ChartType.LineMarkers)
            _planner.SetOverrideChartType(overrideChartType);

        _planner.SetSmoothLine(input.SmoothLine);
        _planner.SetOnSecondaryAxis(input.OnSecondaryAxis);
        _planner.SetInvertIfNegative(input.InvertIfNegative);
        _planner.SetLineWidth(ParseOptional(input.LineWidthText, "Line width"));
        _planner.SetLineColor(input.LineColorText);
        _planner.SetLineDash(ChartDialogOptionProjection.ValueAtOrDefault(
            DashOptions,
            input.LineDashIndex,
            option => option.Value,
            _planner.LineDash));
        _planner.SetNoLine(input.NoLine);
        _planner.SetFillColor(input.FillColorText);
        _planner.SetUseSeriesDataLabels(input.UseSeriesDataLabels);
        _planner.SetShowValueLabels(input.ShowValueLabels);
        _planner.SetShowPercentLabels(input.ShowPercentLabels);
        _planner.SetShowCategoryLabels(input.ShowCategoryLabels);
        _planner.SetShowSeriesLabels(input.ShowSeriesLabels);
        _planner.SetShowLegendKeys(input.ShowLegendKeys);
        _planner.SetShowBubbleSize(input.ShowBubbleSize);
        _planner.SetShowLeaderLines(input.ShowLeaderLines);
        _planner.SetErrorBarsEnabled(input.ErrorBarsEnabled);
        _planner.SetErrorDirection(ChartDialogOptionProjection.ValueAtOrDefault(
            ErrorDirectionOptions,
            input.ErrorDirectionIndex,
            option => option.Value,
            _planner.ErrorDirection));
        _planner.SetErrorBarType(ChartDialogOptionProjection.ValueAtOrDefault(
            ErrorBarTypeOptions,
            input.ErrorBarTypeIndex,
            option => option.Value,
            _planner.ErrorBarType));
        _planner.SetErrorValueType(ChartDialogOptionProjection.ValueAtOrDefault(
            ErrorValueTypeOptions,
            input.ErrorValueTypeIndex,
            option => option.Value,
            _planner.ErrorValueType));
        _planner.SetErrorValue(ParseOptional(input.ErrorValueText, "Error bar value") ?? 0);
        _planner.SetErrorNoEndCap(input.ErrorNoEndCap);
        _planner.SetTrendlineEnabled(input.TrendlineEnabled);
        _planner.SetTrendlineType(ChartDialogOptionProjection.ValueAtOrDefault(
            TrendlineTypeOptions,
            input.TrendlineTypeIndex,
            option => option.Value,
            _planner.TrendlineType));
        _planner.SetTrendlineOrder(ParseOptionalInt(input.TrendlineOrderText, ChartSeriesOptionsPlanner.TrendlineOrderLabel));
        _planner.SetTrendlinePeriod(ParseOptionalInt(input.TrendlinePeriodText, ChartSeriesOptionsPlanner.TrendlinePeriodLabel));
        _planner.SetTrendlineForward(ParseOptional(input.TrendlineForwardText, ChartSeriesOptionsPlanner.TrendlineForwardLabel));
        _planner.SetTrendlineBackward(ParseOptional(input.TrendlineBackwardText, ChartSeriesOptionsPlanner.TrendlineBackwardLabel));
        _planner.SetTrendlineEquation(input.TrendlineEquation);
        _planner.SetTrendlineRSquared(input.TrendlineRSquared);
        _planner.SetLabelPosition(ChartDialogOptionProjection.ValueAtOrDefault(
            LabelPositionOptions,
            input.LabelPositionIndex,
            option => option.Value,
            _planner.LabelPosition));
        _planner.SetLabelNumberFormat(input.LabelNumberFormat);
        _planner.SetLabelSeparator(input.LabelSeparator);
        _planner.SetLabelFontFamily(input.LabelFontFamily);
        _planner.SetLabelFontSize(ParseOptional(input.LabelFontSizeText, "Label font size"));
        _planner.SetLabelBold(input.LabelBold);
        _planner.SetLabelItalic(input.LabelItalic);
        _planner.SetLabelColor(input.LabelColorText);
        _planner.SetMarkerSymbol(ChartDialogOptionProjection.ValueAtOrDefault(
            MarkerOptions,
            input.MarkerIndex,
            option => option.Value,
            _planner.MarkerSymbol));
        _planner.SetMarkerSize(ParseOptional(input.MarkerSizeText, "Marker size"));
        return _planner.BuildCommitPlan();
    }

    public ChartOptionsDialogCommitResult<ChartSeriesOptions> TryCommit(
        ChartSeriesOptionsDialogInput input)
    {
        try
        {
            var plan = BuildCommitPlan(input);
            _editor.ApplyChartSeriesOptions(plan);
            return ChartOptionsDialogCommitResult<ChartSeriesOptions>.Applied(plan);
        }
        catch (FormatException ex)
        {
            return ChartOptionsDialogCommitResult<ChartSeriesOptions>.Invalid(ex.Message);
        }
    }

    private ChartSeriesOptionsDialogState BuildState()
    {
        var seriesChartTypeOptions = BuildSeriesChartTypeOptions(_planner.OverrideChartType);
        return new ChartSeriesOptionsDialogState(
            _planner.SeriesOptions,
            _planner.SeriesIndex,
            seriesChartTypeOptions,
            ChartDialogOptionProjection.FindIndex(
                seriesChartTypeOptions,
                _planner.OverrideChartType,
                option => option.Value),
            _planner.SmoothLine,
            _planner.OnSecondaryAxis,
            _planner.InvertIfNegative,
            Format(_planner.LineWidthPt),
            _planner.LineColorText,
            FindDashIndex(_planner.LineDash),
            _planner.NoLine,
            _planner.FillColorText,
            _planner.UseSeriesDataLabels,
            _planner.ShowValueLabels,
            _planner.ShowPercentLabels,
            _planner.ShowCategoryLabels,
            _planner.ShowSeriesLabels,
            _planner.ShowLegendKeys,
            _planner.ShowBubbleSize,
            _planner.ShowLeaderLines,
            _planner.ErrorBarsEnabled,
            FindErrorDirectionIndex(_planner.ErrorDirection),
            FindErrorBarTypeIndex(_planner.ErrorBarType),
            FindErrorValueTypeIndex(_planner.ErrorValueType),
            Format(_planner.ErrorValue),
            _planner.ErrorNoEndCap,
            _planner.TrendlineEnabled,
            FindTrendlineTypeIndex(_planner.TrendlineType),
            Format(_planner.TrendlineOrder),
            Format(_planner.TrendlinePeriod),
            Format(_planner.TrendlineForward),
            Format(_planner.TrendlineBackward),
            _planner.TrendlineEquation,
            _planner.TrendlineRSquared,
            FindLabelPositionIndex(_planner.LabelPosition),
            _planner.LabelNumberFormat,
            _planner.LabelSeparator,
            _planner.LabelFontFamily,
            Format(_planner.LabelFontSizePt),
            _planner.LabelBold,
            _planner.LabelItalic,
            _planner.LabelColorText,
            FindMarkerIndex(_planner.MarkerSymbol),
            Format(_planner.MarkerSizePt));
    }

    private static IReadOnlyList<ChartSeriesChartTypeOption> BuildSeriesChartTypeOptions(
        ChartType? importedValue)
    {
        if (importedValue is null or ChartType.Line or ChartType.LineMarkers)
            return ChartSeriesOptionsPlanner.SeriesChartTypeOptions;

        return
        [
            .. ChartSeriesOptionsPlanner.SeriesChartTypeOptions,
            new ChartSeriesChartTypeOption(importedValue, $"{importedValue} (imported)"),
        ];
    }

    private double? ParseOptional(string? text, string label) =>
        ChartDialogOptionProjection.ParseOptionalDouble(
            text,
            _culture,
            value => double.IsFinite(value) && value >= 0,
            $"{label} must be a non-negative finite number or blank.");

    private int? ParseOptionalInt(string? text, string label) =>
        ChartDialogOptionProjection.ParseOptionalInt(
            text,
            _culture,
            value => value >= 0,
            $"{label} must be a non-negative integer or blank.");

    private static int FindIndex<TOption, TValue>(
        IReadOnlyList<TOption> options,
        TValue value,
        Func<TOption, TValue> selector) =>
        ChartDialogOptionProjection.FindIndex(options, value, selector);
}
