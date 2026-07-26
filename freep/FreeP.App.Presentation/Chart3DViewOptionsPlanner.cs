using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record Chart3DViewBooleanOption(bool? Value, string Label);

public sealed record Chart3DViewOptionsSurfacePlan(
    string CommandId,
    string Title,
    string RotationXLabel,
    string RotationYLabel,
    string PerspectiveLabel,
    string HeightPercentLabel,
    string DepthPercentLabel,
    string BarGapDepthPercentLabel,
    string RightAngleAxesLabel,
    string WireframeLabel,
    string AutoHint,
    string OkLabel,
    string CancelLabel);

/// <summary>
/// Working-copy planner for the chart camera and Surface3D wireframe controls already supported
/// by the chart model, renderer, and OOXML reader/writer.
/// </summary>
public sealed class Chart3DViewOptionsPlanner
{
    public const string CommandId = "freep.chart.3d-view-options";
    public const string DialogTitle = "Chart 3-D View Options";
    public const string RotationXLabel = "Elevation (degrees)";
    public const string RotationYLabel = "Rotation (degrees)";
    public const string PerspectiveLabel = "Perspective";
    public const string HeightPercentLabel = "Height (%)";
    public const string DepthPercentLabel = "Depth (%)";
    public const string BarGapDepthPercentLabel = "Gap depth (%)";
    public const string RightAngleAxesLabel = "Right-angle axes";
    public const string WireframeLabel = "Surface wireframe";
    public const string AutoHint = "Blank values use the chart default. Gap depth applies to 3-D column/bar charts; wireframe applies to Surface3D charts.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 420;
    public const double DefaultDialogHeight = 360;

    public static IReadOnlyList<Chart3DViewBooleanOption> BooleanOptions { get; } =
    [
        new(null, "Automatic"),
        new(true, "On"),
        new(false, "Off"),
    ];

    private int? _rotationX;
    private int? _rotationY;
    private int? _perspective;
    private int? _heightPercent;
    private int? _depthPercent;
    private int? _barGapDepthPercent;
    private bool? _rightAngleAxes;
    private bool? _wireframe;

    private Chart3DViewOptionsPlanner(ChartShape chart)
    {
        if (chart.View3D is { } view)
        {
            _rotationX = view.RotationX;
            _rotationY = view.RotationY;
            _perspective = view.Perspective;
            _heightPercent = view.HeightPercent;
            _depthPercent = view.DepthPercent;
            _rightAngleAxes = view.RightAngleAxes;
        }

        SupportsBarGapDepth = chart.ThreeDStyle is ChartThreeDStyle.Column or ChartThreeDStyle.Bar;
        _barGapDepthPercent = SupportsBarGapDepth ? chart.BarGapDepthPercent : null;
        _wireframe = chart.WireframeSpecified ? chart.Wireframe : null;
    }

    public static Chart3DViewOptionsSurfacePlan BuildSurfacePlan() =>
        new(
            CommandId,
            DialogTitle,
            RotationXLabel,
            RotationYLabel,
            PerspectiveLabel,
            HeightPercentLabel,
            DepthPercentLabel,
            BarGapDepthPercentLabel,
            RightAngleAxesLabel,
            WireframeLabel,
            AutoHint,
            OkLabel,
            CancelLabel);

    public static Chart3DViewOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new Chart3DViewOptionsPlanner(chart);
    }

    public int? RotationX => _rotationX;
    public int? RotationY => _rotationY;
    public int? Perspective => _perspective;
    public int? HeightPercent => _heightPercent;
    public int? DepthPercent => _depthPercent;
    public int? BarGapDepthPercent => _barGapDepthPercent;
    public bool SupportsBarGapDepth { get; }
    public bool? RightAngleAxes => _rightAngleAxes;
    public bool? Wireframe => _wireframe;

    public void SetRotationX(int? value) => _rotationX = Normalize(value, -90, 90);
    public void SetRotationY(int? value) => _rotationY = Normalize(value, 0, 360);
    public void SetPerspective(int? value) => _perspective = Normalize(value, 0, 240);
    public void SetHeightPercent(int? value) => _heightPercent = Normalize(value, 0, 500);
    public void SetDepthPercent(int? value) => _depthPercent = Normalize(value, 0, 500);
    public void SetBarGapDepthPercent(int? value) => _barGapDepthPercent = SupportsBarGapDepth
        ? Normalize(value, 0, 500)
        : null;
    public void SetRightAngleAxes(bool? value) => _rightAngleAxes = value;
    public void SetWireframe(bool? value) => _wireframe = value;

    public Chart3DViewOptions BuildCommitPlan() => new(
        _rotationX,
        _rotationY,
        _perspective,
        _heightPercent,
        _depthPercent,
        _rightAngleAxes,
        _wireframe,
        _barGapDepthPercent);

    private static int? Normalize(int? value, int minimum, int maximum) =>
        value is null ? null : Math.Clamp(value.Value, minimum, maximum);
}
