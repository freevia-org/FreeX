using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartDialogSessionResult<TOptions>(
    bool ShouldClose,
    TOptions? Options,
    string? ValidationMessage)
    where TOptions : class
{
    internal static ChartDialogSessionResult<TOptions> Accepted(TOptions options) =>
        new(true, options, null);

    internal static ChartDialogSessionResult<TOptions> Invalid(string message) =>
        new(false, null, message);
}

public sealed record Chart3DViewOptionsDialogState(
    string RotationXText,
    string RotationYText,
    string PerspectiveText,
    string HeightPercentText,
    string DepthPercentText,
    string BarGapDepthPercentText,
    int RightAngleAxesIndex,
    int WireframeIndex,
    bool SupportsBarGapDepth);

public sealed record Chart3DViewOptionsDialogInput(
    string? RotationXText,
    string? RotationYText,
    string? PerspectiveText,
    string? HeightPercentText,
    string? DepthPercentText,
    string? BarGapDepthPercentText,
    int RightAngleAxesIndex,
    int WireframeIndex);

public sealed class Chart3DViewOptionsDialogSession
{
    private readonly EditingSession _editor;
    private readonly Chart3DViewOptionsPlanner _planner;
    private readonly CultureInfo _culture;

    public Chart3DViewOptionsDialogSession(
        EditingSession editor,
        CultureInfo? culture = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _culture = culture ?? CultureInfo.CurrentCulture;
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = Chart3DViewOptionsPlanner.FromChart(chart);
        Surface = Chart3DViewOptionsPlanner.BuildSurfacePlan();
        State = new Chart3DViewOptionsDialogState(
            Format(_planner.RotationX),
            Format(_planner.RotationY),
            Format(_planner.Perspective),
            Format(_planner.HeightPercent),
            Format(_planner.DepthPercent),
            Format(_planner.BarGapDepthPercent),
            FindBooleanIndex(_planner.RightAngleAxes),
            FindBooleanIndex(_planner.Wireframe),
            _planner.SupportsBarGapDepth);
    }

    public Chart3DViewOptionsSurfacePlan Surface { get; }

    public Chart3DViewOptionsDialogState State { get; }

    public IReadOnlyList<Chart3DViewBooleanOption> BooleanOptions =>
        Chart3DViewOptionsPlanner.BooleanOptions;

    public string Format(int? value) =>
        ChartDialogOptionProjection.Format(value, _culture);

    public int FindBooleanIndex(bool? value) =>
        ChartDialogOptionProjection.FindIndex(
            BooleanOptions,
            value,
            option => option.Value);

    public Chart3DViewOptions BuildCommitPlan(Chart3DViewOptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rotationX = ParseOptionalInt(input.RotationXText, "Elevation", -90, 90);
        var rotationY = ParseOptionalInt(input.RotationYText, "Rotation", 0, 360);
        var perspective = ParseOptionalInt(input.PerspectiveText, "Perspective", 0, 240);
        var height = ParseOptionalInt(input.HeightPercentText, "Height", 0, 500);
        var depth = ParseOptionalInt(input.DepthPercentText, "Depth", 0, 500);
        var gapDepth = ParseOptionalInt(input.BarGapDepthPercentText, "Gap depth", 0, 500);

        _planner.SetRotationX(rotationX);
        _planner.SetRotationY(rotationY);
        _planner.SetPerspective(perspective);
        _planner.SetHeightPercent(height);
        _planner.SetDepthPercent(depth);
        _planner.SetBarGapDepthPercent(gapDepth);
        _planner.SetRightAngleAxes(ReadBoolean(input.RightAngleAxesIndex));
        _planner.SetWireframe(ReadBoolean(input.WireframeIndex));
        return _planner.BuildCommitPlan();
    }

    public ChartDialogSessionResult<Chart3DViewOptions> Submit(
        Chart3DViewOptionsDialogInput input)
    {
        try
        {
            var options = BuildCommitPlan(input);
            _editor.ApplyChart3DViewOptions(options);
            return ChartDialogSessionResult<Chart3DViewOptions>.Accepted(options);
        }
        catch (FormatException ex)
        {
            return ChartDialogSessionResult<Chart3DViewOptions>.Invalid(ex.Message);
        }
    }

    private bool? ReadBoolean(int selectedIndex) =>
        ChartDialogOptionProjection.ValueAtOrDefault(
            BooleanOptions,
            selectedIndex,
            option => option.Value,
            default(bool?));

    private int? ParseOptionalInt(
        string? text,
        string surface,
        int minimum,
        int maximum) =>
        ChartDialogOptionProjection.ParseOptionalInt(
            text,
            _culture,
            value => value >= minimum && value <= maximum,
            $"{surface} must be a whole number from {minimum} to {maximum}, or blank.");
}

public sealed record ChartBubbleOptionsDialogState(
    string BubbleScaleText,
    int SizeRepresentsIndex,
    bool ShowNegativeBubbles);

public sealed record ChartBubbleOptionsDialogInput(
    string? BubbleScaleText,
    int SizeRepresentsIndex,
    bool ShowNegativeBubbles);

public sealed class ChartBubbleOptionsDialogSession
{
    private readonly EditingSession _editor;
    private readonly ChartBubbleOptionsPlanner _planner;
    private readonly CultureInfo _culture;

    public ChartBubbleOptionsDialogSession(
        EditingSession editor,
        CultureInfo? culture = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _culture = culture ?? CultureInfo.CurrentCulture;
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        if (chart.ChartType != ChartType.Bubble)
            throw new InvalidOperationException("Select a bubble chart before editing bubble options.");

        _planner = ChartBubbleOptionsPlanner.FromChart(chart);
        Surface = ChartBubbleOptionsPlanner.BuildSurfacePlan();
        State = new ChartBubbleOptionsDialogState(
            Format(_planner.BubbleScalePercent),
            FindSizeRepresentsIndex(_planner.SizeRepresents),
            _planner.ShowNegativeBubbles);
    }

    public ChartBubbleOptionsSurfacePlan Surface { get; }

    public ChartBubbleOptionsDialogState State { get; }

    public IReadOnlyList<ChartBubbleSizeRepresentationOption> SizeRepresentsOptions =>
        ChartBubbleOptionsPlanner.SizeRepresentsOptions;

    public string Format(int value) => value.ToString(_culture);

    public int FindSizeRepresentsIndex(BubbleSizeRepresentation value) =>
        ChartDialogOptionProjection.FindIndex(
            SizeRepresentsOptions,
            value,
            option => option.Value);

    public ChartBubbleOptions BuildCommitPlan(ChartBubbleOptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var scale = ChartDialogOptionProjection.ParseRequiredInt(
            input.BubbleScaleText,
            _culture,
            value => value is >= 0 and <= 300,
            "Bubble scale must be a whole number from 0 to 300.");
        var sizeRepresents = ChartDialogOptionProjection.ValueAtOrDefault(
            SizeRepresentsOptions,
            input.SizeRepresentsIndex,
            option => option.Value,
            BubbleSizeRepresentation.Area);

        _planner.SetBubbleScalePercent(scale);
        _planner.SetSizeRepresents(sizeRepresents);
        _planner.SetShowNegativeBubbles(input.ShowNegativeBubbles);
        return _planner.BuildCommitPlan();
    }

    public ChartDialogSessionResult<ChartBubbleOptions> Submit(
        ChartBubbleOptionsDialogInput input)
    {
        try
        {
            var options = BuildCommitPlan(input);
            _editor.ApplyChartBubbleOptions(options);
            return ChartDialogSessionResult<ChartBubbleOptions>.Accepted(options);
        }
        catch (FormatException ex)
        {
            return ChartDialogSessionResult<ChartBubbleOptions>.Invalid(ex.Message);
        }
    }
}

public sealed record ChartPlotStyleOptionsDialogState(
    int ScatterStyleIndex,
    int RadarStyleIndex,
    bool IsScatterEnabled,
    bool IsRadarEnabled);

public sealed record ChartPlotStyleOptionsDialogInput(
    int ScatterStyleIndex,
    int RadarStyleIndex);

public sealed class ChartPlotStyleOptionsDialogSession
{
    private readonly EditingSession _editor;
    private readonly ChartPlotStyleOptionsPlanner _planner;

    public ChartPlotStyleOptionsDialogSession(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        if (chart.ChartType is not (ChartType.Scatter or ChartType.Radar))
            throw new InvalidOperationException("Select a Scatter or Radar chart before editing plot style.");

        _planner = ChartPlotStyleOptionsPlanner.FromChart(chart);
        Surface = ChartPlotStyleOptionsPlanner.BuildSurfacePlan();
        State = new ChartPlotStyleOptionsDialogState(
            FindScatterIndex(_planner.ScatterStyle),
            FindRadarIndex(_planner.RadarStyle),
            chart.ChartType == ChartType.Scatter,
            chart.ChartType == ChartType.Radar);
    }

    public ChartPlotStyleOptionsSurfacePlan Surface { get; }

    public ChartPlotStyleOptionsDialogState State { get; }

    public IReadOnlyList<ChartScatterStyleOption> ScatterStyleOptions =>
        ChartPlotStyleOptionsPlanner.ScatterStyleOptions;

    public IReadOnlyList<ChartRadarStyleOption> RadarStyleOptions =>
        ChartPlotStyleOptionsPlanner.RadarStyleOptions;

    public int FindScatterIndex(ScatterStyle value) =>
        ChartDialogOptionProjection.FindIndex(
            ScatterStyleOptions,
            value,
            option => option.Value);

    public int FindRadarIndex(RadarStyle value) =>
        ChartDialogOptionProjection.FindIndex(
            RadarStyleOptions,
            value,
            option => option.Value);

    public ChartPlotStyleOptions BuildCommitPlan(ChartPlotStyleOptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _planner.SetScatterStyle(ChartDialogOptionProjection.ValueAtOrDefault(
            ScatterStyleOptions,
            input.ScatterStyleIndex,
            option => option.Value,
            _planner.ScatterStyle));
        _planner.SetRadarStyle(ChartDialogOptionProjection.ValueAtOrDefault(
            RadarStyleOptions,
            input.RadarStyleIndex,
            option => option.Value,
            _planner.RadarStyle));
        return _planner.BuildCommitPlan();
    }

    public ChartDialogSessionResult<ChartPlotStyleOptions> Submit(
        ChartPlotStyleOptionsDialogInput input)
    {
        var options = BuildCommitPlan(input);
        _editor.ApplyChartPlotStyleOptions(options);
        return ChartDialogSessionResult<ChartPlotStyleOptions>.Accepted(options);
    }
}

public sealed record ChartProtectionOptionsDialogState(
    int ChartObjectIndex,
    int DataIndex,
    int FormattingIndex,
    int SelectionIndex);

public sealed record ChartProtectionOptionsDialogInput(
    int ChartObjectIndex,
    int DataIndex,
    int FormattingIndex,
    int SelectionIndex);

public sealed class ChartProtectionOptionsDialogSession
{
    private readonly EditingSession _editor;
    private readonly ChartProtectionOptionsPlanner _planner;

    public ChartProtectionOptionsDialogSession(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartProtectionOptionsPlanner.FromChart(chart);
        Surface = ChartProtectionOptionsPlanner.BuildSurfacePlan();
        State = new ChartProtectionOptionsDialogState(
            FindBooleanIndex(_planner.ChartObject),
            FindBooleanIndex(_planner.Data),
            FindBooleanIndex(_planner.Formatting),
            FindBooleanIndex(_planner.Selection));
    }

    public ChartProtectionOptionsSurfacePlan Surface { get; }

    public ChartProtectionOptionsDialogState State { get; }

    public IReadOnlyList<ChartProtectionBooleanOption> BooleanOptions =>
        ChartProtectionOptionsPlanner.BooleanOptions;

    public int FindBooleanIndex(bool? value) =>
        ChartDialogOptionProjection.FindIndex(
            BooleanOptions,
            value,
            option => option.Value);

    public ChartProtectionOptions BuildCommitPlan(ChartProtectionOptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _planner.SetChartObject(ReadBoolean(input.ChartObjectIndex));
        _planner.SetData(ReadBoolean(input.DataIndex));
        _planner.SetFormatting(ReadBoolean(input.FormattingIndex));
        _planner.SetSelection(ReadBoolean(input.SelectionIndex));
        return _planner.BuildCommitPlan();
    }

    public ChartDialogSessionResult<ChartProtectionOptions> Submit(
        ChartProtectionOptionsDialogInput input)
    {
        var options = BuildCommitPlan(input);
        _editor.ApplyChartProtectionOptions(options);
        return ChartDialogSessionResult<ChartProtectionOptions>.Accepted(options);
    }

    private bool? ReadBoolean(int selectedIndex) =>
        ChartDialogOptionProjection.ValueAtOrDefault(
            BooleanOptions,
            selectedIndex,
            option => option.Value,
            default(bool?));
}

public sealed record ChartTextOptionsDialogState(
    string FontFamilyText,
    string FontSizeText,
    int BoldIndex,
    int ItalicIndex,
    string ColorText);

public sealed record ChartTextOptionsDialogInput(
    string? FontFamilyText,
    string? FontSizeText,
    int BoldIndex,
    int ItalicIndex,
    string? ColorText);

public sealed class ChartTextOptionsDialogSession
{
    private readonly EditingSession _editor;
    private readonly ChartTextOptionsPlanner _planner;
    private readonly CultureInfo _culture;

    public ChartTextOptionsDialogSession(
        EditingSession editor,
        ChartTextTarget target = ChartTextTarget.Chart,
        CultureInfo? culture = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _culture = culture ?? CultureInfo.CurrentCulture;
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartTextOptionsPlanner.FromChart(chart, target);
        Surface = ChartTextOptionsPlanner.BuildSurfacePlan(target);
        State = new ChartTextOptionsDialogState(
            _planner.FontFamily ?? string.Empty,
            FormatFontSize(_planner.FontSizePt),
            FindBooleanIndex(_planner.Bold),
            FindBooleanIndex(_planner.Italic),
            _planner.ColorText);
    }

    public ChartTextOptionsSurfacePlan Surface { get; }

    public ChartTextOptionsDialogState State { get; }

    public IReadOnlyList<ChartTextBooleanOption> BooleanOptions =>
        ChartTextOptionsPlanner.BooleanOptions;

    public string FormatFontSize(double? value) =>
        ChartDialogOptionProjection.Format(value, _culture);

    public int FindBooleanIndex(bool? value) =>
        ChartDialogOptionProjection.FindIndex(
            BooleanOptions,
            value,
            option => option.Value);

    public ChartTextOptions BuildCommitPlan(ChartTextOptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var fontSize = ParseOptionalFontSize(input.FontSizeText, _culture);
        var color = ChartPointOptionsPlanner.ParseColor(
            input.ColorText,
            ChartTextOptionsPlanner.ColorLabel);

        _planner.SetFontFamily(input.FontFamilyText);
        _planner.SetFontSizePt(fontSize);
        _planner.SetBold(ReadBoolean(input.BoldIndex));
        _planner.SetItalic(ReadBoolean(input.ItalicIndex));
        _planner.SetColor(color);
        return _planner.BuildCommitPlan();
    }

    public ChartDialogSessionResult<ChartTextOptions> Submit(
        ChartTextOptionsDialogInput input)
    {
        try
        {
            var options = BuildCommitPlan(input);
            _editor.ApplyChartTextOptions(options);
            return ChartDialogSessionResult<ChartTextOptions>.Accepted(options);
        }
        catch (FormatException ex)
        {
            return ChartDialogSessionResult<ChartTextOptions>.Invalid(ex.Message);
        }
    }

    public static double? ParseOptionalFontSize(string? text, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return ChartDialogOptionProjection.ParseOptionalDouble(
            text,
            culture,
            value => double.IsFinite(value) && value is >= 1 and <= 400,
            $"{ChartTextOptionsPlanner.FontSizeLabel} must be a number from 1 to 400, or blank.");
    }

    private bool? ReadBoolean(int selectedIndex) =>
        ChartDialogOptionProjection.ValueAtOrDefault(
            BooleanOptions,
            selectedIndex,
            option => option.Value,
            default(bool?));
}
