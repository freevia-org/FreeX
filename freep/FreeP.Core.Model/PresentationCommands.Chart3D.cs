namespace FreeP.Core.Model;

/// <summary>Atomically updates chart camera and Surface3D wireframe options.</summary>
public sealed class SetChart3DViewOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly Chart3DViewOptions _newOptions;
    private Chart3DViewOptions? _oldOptions;

    public SetChart3DViewOptionsCommand(
        int slideIndex,
        uint shapeId,
        Chart3DViewOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Chart 3-D View";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null)
            return;

        _oldOptions = ReadOptions(chart);
        chart.View3D = BuildView(_newOptions);
        chart.WireframeSpecified = _newOptions.Wireframe.HasValue;
        chart.Wireframe = _newOptions.Wireframe == true;
        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (chart is null || _oldOptions is null)
            return;

        chart.View3D = BuildView(_oldOptions);
        chart.WireframeSpecified = _oldOptions.Wireframe.HasValue;
        chart.Wireframe = _oldOptions.Wireframe == true;
        ChartHelper.MarkWorkbookDirty(chart);
    }

    private static Chart3DViewOptions ReadOptions(ChartShape chart)
    {
        var view = chart.View3D;
        return new(
            view?.RotationX,
            view?.RotationY,
            view?.Perspective,
            view?.HeightPercent,
            view?.DepthPercent,
            view?.RightAngleAxes,
            chart.WireframeSpecified ? chart.Wireframe : null);
    }

    private static Chart3DView? BuildView(Chart3DViewOptions options)
    {
        if (options.RotationX is null &&
            options.RotationY is null &&
            options.Perspective is null &&
            options.HeightPercent is null &&
            options.DepthPercent is null &&
            options.RightAngleAxes is null)
            return null;

        return new Chart3DView
        {
            RotationX = Normalize(options.RotationX, -90, 90),
            RotationY = Normalize(options.RotationY, 0, 360),
            Perspective = Normalize(options.Perspective, 0, 240),
            HeightPercent = Normalize(options.HeightPercent, 0, 500),
            DepthPercent = Normalize(options.DepthPercent, 0, 500),
            RightAngleAxes = options.RightAngleAxes,
        };
    }

    private static int? Normalize(int? value, int minimum, int maximum) =>
        value is null ? null : Math.Clamp(value.Value, minimum, maximum);
}
