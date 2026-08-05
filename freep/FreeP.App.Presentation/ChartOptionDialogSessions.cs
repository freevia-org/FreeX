using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartOptionsDialogCommitResult<TPlan>(
    bool Succeeded,
    TPlan? CommitPlan,
    string Error)
    where TPlan : class
{
    internal static ChartOptionsDialogCommitResult<TPlan> Applied(TPlan plan) =>
        new(true, plan, string.Empty);

    internal static ChartOptionsDialogCommitResult<TPlan> Invalid(string error) =>
        new(false, null, error);
}

public sealed record ChartAreaOptionsDialogState(
    int TargetIndex,
    string FillColor,
    double? FillTransparencyPercent,
    bool NoFill,
    string OutlineColor,
    bool NoOutline,
    double? OutlineWidthPt);

public sealed record ChartAreaOptionsDialogInput(
    int TargetIndex,
    string? FillColor,
    string? FillTransparency,
    bool NoFill,
    string? OutlineColor,
    bool NoOutline,
    string? OutlineWidth);

public sealed class ChartAreaOptionsDialogSession
{
    private readonly EditingSession _editor;
    private readonly ChartAreaOptionsPlanner _planner;

    public ChartAreaOptionsDialogSession(
        EditingSession editor,
        ChartAreaFormattingTarget? initialTarget = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _planner = ChartAreaOptionsPlanner.FromChart(
            editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected."));
        if (initialTarget is { } target)
            _planner.SetTarget(target);
    }

    public ChartAreaOptionsDialogState State => BuildState();

    public ChartAreaOptionsDialogState SelectTarget(int targetIndex)
    {
        _planner.SetTarget(TargetAt(targetIndex));
        return BuildState();
    }

    public ChartAreaOptions BuildCommitPlan(
        ChartAreaOptionsDialogInput input,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        _planner.SetTarget(TargetAt(input.TargetIndex));
        _planner.SetFillColor(input.FillColor);
        _planner.SetFillTransparency(ChartDialogOptionProjection.ParseOptionalDouble(
            input.FillTransparency,
            culture,
            double.IsFinite,
            "The value must be a finite number or blank."));
        _planner.SetNoFill(input.NoFill);
        _planner.SetOutlineColor(input.OutlineColor);
        _planner.SetNoOutline(input.NoOutline);
        _planner.SetOutlineWidth(ChartDialogOptionProjection.ParseOptionalDouble(
            input.OutlineWidth,
            culture,
            double.IsFinite,
            "The value must be a finite number or blank."));
        return _planner.BuildCommitPlan();
    }

    public ChartOptionsDialogCommitResult<ChartAreaOptions> TryCommit(
        ChartAreaOptionsDialogInput input,
        CultureInfo culture)
    {
        try
        {
            var plan = BuildCommitPlan(input, culture);
            _editor.ApplyChartAreaOptions(plan);
            return ChartOptionsDialogCommitResult<ChartAreaOptions>.Applied(plan);
        }
        catch (FormatException ex)
        {
            return ChartOptionsDialogCommitResult<ChartAreaOptions>.Invalid(ex.Message);
        }
    }

    private ChartAreaOptionsDialogState BuildState() => new(
        ChartDialogOptionProjection.FindIndex(
            ChartAreaOptionsPlanner.TargetOptions,
            _planner.Target,
            option => option.Value),
        _planner.FillColor,
        _planner.FillTransparencyPercent,
        _planner.NoFill,
        _planner.OutlineColor,
        _planner.NoOutline,
        _planner.OutlineWidthPt);

    private static ChartAreaFormattingTarget TargetAt(int targetIndex) =>
        ChartDialogOptionProjection.ValueAtOrDefault(
            ChartAreaOptionsPlanner.TargetOptions,
            targetIndex,
            option => option.Value,
            ChartAreaFormattingTarget.ChartArea);
}

public sealed record ChartDataTableOptionsDialogState(
    bool ShowDataTable,
    bool ShowHorizontalBorder,
    bool ShowVerticalBorder,
    bool ShowOutlineBorder,
    bool ShowLegendKeys,
    string BackgroundColor,
    string BorderColor,
    double? BorderWidthPt,
    string TextColor,
    double? FontSizePt,
    string FontFamily,
    bool? Bold,
    bool? Italic);

public sealed record ChartDataTableOptionsDialogInput(
    bool ShowDataTable,
    bool ShowHorizontalBorder,
    bool ShowVerticalBorder,
    bool ShowOutlineBorder,
    bool ShowLegendKeys,
    string? BackgroundColor,
    string? BorderColor,
    string? BorderWidth,
    string? TextColor,
    string? FontSize,
    string? FontFamily,
    bool? Bold,
    bool? Italic);

public sealed class ChartDataTableOptionsDialogSession
{
    private readonly EditingSession _editor;
    private readonly ChartDataTableOptionsPlanner _planner;

    public ChartDataTableOptionsDialogSession(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _planner = ChartDataTableOptionsPlanner.FromChart(
            editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected."));
    }

    public ChartDataTableOptionsDialogState State => new(
        _planner.ShowDataTable,
        _planner.ShowHorizontalBorder,
        _planner.ShowVerticalBorder,
        _planner.ShowOutlineBorder,
        _planner.ShowLegendKeys,
        _planner.BackgroundColor,
        _planner.BorderColor,
        _planner.BorderWidthPt,
        _planner.TextColor,
        _planner.FontSizePt,
        _planner.FontFamily,
        _planner.Bold,
        _planner.Italic);

    public ChartDataTableOptions BuildCommitPlan(
        ChartDataTableOptionsDialogInput input,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        _planner.SetShowDataTable(input.ShowDataTable);
        _planner.SetShowHorizontalBorder(input.ShowHorizontalBorder);
        _planner.SetShowVerticalBorder(input.ShowVerticalBorder);
        _planner.SetShowOutlineBorder(input.ShowOutlineBorder);
        _planner.SetShowLegendKeys(input.ShowLegendKeys);
        _planner.SetBackgroundColor(input.BackgroundColor);
        _planner.SetBorderColor(input.BorderColor);
        _planner.SetBorderWidth(ParsePositiveOptional(input.BorderWidth, culture, "Border width"));
        _planner.SetTextColor(input.TextColor);
        _planner.SetFontSize(ParsePositiveOptional(input.FontSize, culture, "Font size"));
        _planner.SetFontFamily(input.FontFamily);
        _planner.SetBold(input.Bold);
        _planner.SetItalic(input.Italic);
        return _planner.BuildCommitPlan();
    }

    public ChartOptionsDialogCommitResult<ChartDataTableOptions> TryCommit(
        ChartDataTableOptionsDialogInput input,
        CultureInfo culture)
    {
        try
        {
            var plan = BuildCommitPlan(input, culture);
            _editor.ApplyChartDataTableOptions(plan);
            return ChartOptionsDialogCommitResult<ChartDataTableOptions>.Applied(plan);
        }
        catch (FormatException ex)
        {
            return ChartOptionsDialogCommitResult<ChartDataTableOptions>.Invalid(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ChartOptionsDialogCommitResult<ChartDataTableOptions>.Invalid(ex.Message);
        }
    }

    private static double? ParsePositiveOptional(
        string? text,
        CultureInfo culture,
        string label) =>
        ChartDialogOptionProjection.ParseOptionalDouble(
            text,
            culture,
            value => double.IsFinite(value) && value > 0,
            $"{label} must be a positive finite number or blank.");
}

public sealed record ChartLayoutOptionsDialogState(
    int TargetIndex,
    IReadOnlyList<ChartLayoutTargetSemanticOption> LayoutTargetOptions,
    int LayoutTargetIndex,
    int XModeIndex,
    int YModeIndex,
    int WidthModeIndex,
    int HeightModeIndex,
    double? X,
    double? Y,
    double? Width,
    double? Height);

public sealed record ChartLayoutOptionsDialogInput(
    int TargetIndex,
    int LayoutTargetIndex,
    int XModeIndex,
    int YModeIndex,
    int WidthModeIndex,
    int HeightModeIndex,
    string? X,
    string? Y,
    string? Width,
    string? Height);

public sealed class ChartLayoutOptionsDialogSession
{
    private readonly EditingSession _editor;
    private readonly ChartLayoutOptionsPlanner _planner;

    public ChartLayoutOptionsDialogSession(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _planner = ChartLayoutOptionsPlanner.FromChart(
            editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected."));
    }

    public ChartLayoutOptionsDialogState State => BuildState();

    public ChartLayoutOptionsDialogState SelectTarget(int targetIndex)
    {
        _planner.SetTarget(TargetAt(targetIndex));
        return BuildState();
    }

    public ChartLayoutOptions BuildCommitPlan(
        ChartLayoutOptionsDialogInput input,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        _planner.SetTarget(TargetAt(input.TargetIndex));
        var layoutTargetOptions = ChartLayoutOptionsPlanner.LayoutTargetOptionsFor(_planner.LayoutTarget);
        _planner.SetLayoutTarget(ChartDialogOptionProjection.ValueAtOrDefault(
            layoutTargetOptions,
            input.LayoutTargetIndex,
            option => option.Value,
            default(string?)));
        _planner.SetXMode(ModeAt(input.XModeIndex));
        _planner.SetYMode(ModeAt(input.YModeIndex));
        _planner.SetWidthMode(ModeAt(input.WidthModeIndex));
        _planner.SetHeightMode(ModeAt(input.HeightModeIndex));
        _planner.SetX(ParseOptional(input.X, culture, "X"));
        _planner.SetY(ParseOptional(input.Y, culture, "Y"));
        _planner.SetWidth(ParseOptional(input.Width, culture, "Width"));
        _planner.SetHeight(ParseOptional(input.Height, culture, "Height"));
        return _planner.BuildCommitPlan();
    }

    public ChartOptionsDialogCommitResult<ChartLayoutOptions> TryCommit(
        ChartLayoutOptionsDialogInput input,
        CultureInfo culture)
    {
        try
        {
            var plan = BuildCommitPlan(input, culture);
            _editor.ApplyChartLayoutOptions(plan);
            return ChartOptionsDialogCommitResult<ChartLayoutOptions>.Applied(plan);
        }
        catch (FormatException ex)
        {
            return ChartOptionsDialogCommitResult<ChartLayoutOptions>.Invalid(ex.Message);
        }
    }

    private ChartLayoutOptionsDialogState BuildState()
    {
        var layoutTargetOptions = ChartLayoutOptionsPlanner.LayoutTargetOptionsFor(_planner.LayoutTarget);
        return new ChartLayoutOptionsDialogState(
            ChartDialogOptionProjection.FindIndex(
                ChartLayoutOptionsPlanner.TargetOptions,
                _planner.Target,
                option => option.Value),
            layoutTargetOptions,
            ChartDialogOptionProjection.FindIndex(
                layoutTargetOptions,
                _planner.LayoutTarget,
                option => option.Value,
                comparer: StringComparer.OrdinalIgnoreCase),
            ModeIndex(_planner.XMode),
            ModeIndex(_planner.YMode),
            ModeIndex(_planner.WidthMode),
            ModeIndex(_planner.HeightMode),
            _planner.X,
            _planner.Y,
            _planner.Width,
            _planner.Height);
    }

    private static ChartLayoutTarget TargetAt(int targetIndex) =>
        ChartDialogOptionProjection.ValueAtOrDefault(
            ChartLayoutOptionsPlanner.TargetOptions,
            targetIndex,
            option => option.Value,
            ChartLayoutTarget.PlotArea);

    private static ChartManualLayoutMode ModeAt(int modeIndex) =>
        ChartDialogOptionProjection.ValueAtOrDefault(
            ChartLayoutOptionsPlanner.ModeOptions,
            modeIndex,
            option => option.Value,
            ChartManualLayoutMode.Factor);

    private static int ModeIndex(ChartManualLayoutMode mode) =>
        ChartDialogOptionProjection.FindIndex(
            ChartLayoutOptionsPlanner.ModeOptions,
            mode,
            option => option.Value);

    private static double? ParseOptional(
        string? text,
        CultureInfo culture,
        string label) =>
        ChartDialogOptionProjection.ParseOptionalDouble(
            text,
            culture,
            double.IsFinite,
            $"{label} must be a finite number or blank.");
}

public sealed record ChartPieOptionsDialogState(
    ChartType ChartType,
    IReadOnlyList<string> OfPieTypeOptions,
    IReadOnlyList<string> OfPieSplitTypeOptions,
    int? FirstSliceAngleDegrees,
    int DoughnutHolePercent,
    int OfPieTypeIndex,
    int OfPieSplitTypeIndex,
    double? OfPieSplitPosition,
    int OfPieSecondPieSizePercent,
    IReadOnlyList<int> OfPieCustomPointIndices,
    int? OfPieGapWidthPercent,
    bool OfPieSeriesLines)
{
    public bool IsDoughnut => ChartType == ChartType.Doughnut;
    public bool IsOfPie => ChartType == ChartType.OfPie;
}

public sealed record ChartPieOptionsDialogInput(
    string? FirstSliceAngle,
    string? DoughnutHole,
    int OfPieTypeIndex,
    int OfPieSplitTypeIndex,
    string? OfPieSplitPosition,
    string? OfPieSecondPieSize,
    string? OfPieCustomPointIndices,
    string? OfPieGapWidth,
    bool OfPieSeriesLines);

public sealed class ChartPieOptionsDialogSession
{
    private static readonly IReadOnlyList<string> OfPieTypeOptions = ["Pie", "Bar"];
    private static readonly IReadOnlyList<string> OfPieSplitTypeOptions = ["Auto", "Custom", "Percent", "Position", "Value"];

    private readonly EditingSession _editor;
    private readonly ChartPieOptionsPlanner _planner;
    private readonly ChartType _chartType;

    public ChartPieOptionsDialogSession(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartPieOptionsPlanner.FromChart(chart);
        _chartType = chart.ChartType;
    }

    public ChartPieOptionsDialogState State => new(
        _chartType,
        OfPieTypeOptions,
        OfPieSplitTypeOptions,
        _planner.FirstSliceAngleDegrees,
        _planner.DoughnutHolePercent,
        _planner.OfPieType == OfPieType.Bar ? 1 : 0,
        (int)_planner.OfPieSplitType,
        _planner.OfPieSplitPosition,
        _planner.OfPieSecondPieSizePercent,
        _planner.OfPieCustomPointIndices,
        _planner.OfPieGapWidthPercent,
        _planner.OfPieSeriesLines);

    public ChartPieOptions BuildCommitPlan(
        ChartPieOptionsDialogInput input,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        _planner.SetFirstSliceAngleDegrees(ChartDialogOptionProjection.ParseRequiredInt(
            input.FirstSliceAngle,
            culture,
            value => value is >= 0 and <= 359,
            "First slice angle must be a whole number from 0 to 359."));
        _planner.SetDoughnutHolePercent(ChartDialogOptionProjection.ParseRequiredInt(
            input.DoughnutHole,
            culture,
            value => value is >= 10 and <= 90,
            "Doughnut hole must be a whole number from 10 to 90."));

        if (_chartType == ChartType.OfPie)
        {
            _planner.SetOfPieType(input.OfPieTypeIndex == 1 ? OfPieType.Bar : OfPieType.Pie);
            _planner.SetOfPieSplitType((OfPieSplitType)Math.Clamp(input.OfPieSplitTypeIndex, 0, 4));
            _planner.SetOfPieSplitPosition(ChartDialogOptionProjection.ParseOptionalDouble(
                input.OfPieSplitPosition,
                culture,
                value => value >= 0,
                "OfPie split position must be a non-negative number or blank."));
            _planner.SetOfPieSecondPieSizePercent(ChartDialogOptionProjection.ParseRequiredInt(
                input.OfPieSecondPieSize,
                culture,
                value => value is >= 5 and <= 200,
                "Secondary plot size must be a whole number from 5 to 200."));
            _planner.SetOfPieCustomPointIndices(ChartDialogOptionProjection.ParseNonNegativeIntList(
                input.OfPieCustomPointIndices,
                culture,
                "Custom secondary points must be non-negative whole numbers separated by commas."));
            _planner.SetOfPieGapWidthPercent(ChartDialogOptionProjection.ParseOptionalInt(
                input.OfPieGapWidth,
                culture,
                value => value is >= 0 and <= 500,
                "Secondary plot gap width must be a whole number from 0 to 500, or blank."));
            _planner.SetOfPieSeriesLines(input.OfPieSeriesLines);
        }

        return _planner.BuildCommitPlan();
    }

    public ChartOptionsDialogCommitResult<ChartPieOptions> TryCommit(
        ChartPieOptionsDialogInput input,
        CultureInfo culture)
    {
        try
        {
            var plan = BuildCommitPlan(input, culture);
            _editor.ApplyChartPieOptions(plan);
            return ChartOptionsDialogCommitResult<ChartPieOptions>.Applied(plan);
        }
        catch (FormatException ex)
        {
            return ChartOptionsDialogCommitResult<ChartPieOptions>.Invalid(ex.Message);
        }
    }
}
