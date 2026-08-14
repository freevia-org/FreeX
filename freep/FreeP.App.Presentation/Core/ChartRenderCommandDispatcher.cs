namespace FreeP.App.Compositor;

public interface IChartRenderCommandSink
{
    void Render(ChartRenderCommand.Frame command);
    void Render(ChartRenderCommand.Rectangle command);
    void Render(ChartRenderCommand.Line command);
    void Render(ChartRenderCommand.Path command);
    void Render(ChartRenderCommand.LinePath command);
    void Render(ChartRenderCommand.Marker command);
    void Render(ChartRenderCommand.PieSlice command);
    void Render(ChartRenderCommand.DoughnutSlice command);
    void Render(ChartRenderCommand.SurfaceFacet command);
    void Render(ChartRenderCommand.Bubble command);
    void Render(ChartRenderCommand.Text command);
}

/// <summary>
/// Dispatches the portable chart paint list to a renderer-owned drawing sink.
/// </summary>
public static class ChartRenderCommandDispatcher
{
    public static void Dispatch(
        IReadOnlyList<ChartRenderCommand> commands,
        IChartRenderCommandSink sink)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(sink);

        foreach (var command in commands)
        {
            switch (command)
            {
                case ChartRenderCommand.Frame value: sink.Render(value); break;
                case ChartRenderCommand.Rectangle value: sink.Render(value); break;
                case ChartRenderCommand.Line value: sink.Render(value); break;
                case ChartRenderCommand.Path value: sink.Render(value); break;
                case ChartRenderCommand.LinePath value: sink.Render(value); break;
                case ChartRenderCommand.Marker value: sink.Render(value); break;
                case ChartRenderCommand.PieSlice value: sink.Render(value); break;
                case ChartRenderCommand.DoughnutSlice value: sink.Render(value); break;
                case ChartRenderCommand.SurfaceFacet value: sink.Render(value); break;
                case ChartRenderCommand.Bubble value: sink.Render(value); break;
                case ChartRenderCommand.Text value: sink.Render(value); break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(commands),
                        command,
                        "Unknown chart render command.");
            }
        }
    }
}

public interface IChartMarkerRenderPrimitiveSink
{
    void Render(ChartMarkerRenderPrimitive.Ellipse primitive);
    void Render(ChartMarkerRenderPrimitive.Rectangle primitive);
    void Render(ChartMarkerRenderPrimitive.Path primitive);
    void Render(ChartMarkerRenderPrimitive.Line primitive);
}

public static class ChartMarkerRenderPrimitiveDispatcher
{
    public static void Dispatch(
        IReadOnlyList<ChartMarkerRenderPrimitive> primitives,
        IChartMarkerRenderPrimitiveSink sink)
    {
        ArgumentNullException.ThrowIfNull(primitives);
        ArgumentNullException.ThrowIfNull(sink);

        foreach (var primitive in primitives)
        {
            switch (primitive)
            {
                case ChartMarkerRenderPrimitive.Ellipse value: sink.Render(value); break;
                case ChartMarkerRenderPrimitive.Rectangle value: sink.Render(value); break;
                case ChartMarkerRenderPrimitive.Path value: sink.Render(value); break;
                case ChartMarkerRenderPrimitive.Line value: sink.Render(value); break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(primitives),
                        primitive,
                        "Unknown chart marker render primitive.");
            }
        }
    }
}
