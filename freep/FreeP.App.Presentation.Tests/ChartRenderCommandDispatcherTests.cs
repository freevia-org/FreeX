namespace FreeP.App.Compositor.Tests;

public sealed class ChartRenderCommandDispatcherTests
{
    [Fact]
    public void DispatchPreservesPortablePaintOrderAndConcreteCommandType()
    {
        ChartRenderCommand[] commands =
        [
            new ChartRenderCommand.Frame(
                new ChartPlanRect(1, 2, 3, 4),
                new ChartFillPlan(SrgbColor.Black, 255),
                null,
                RoundedCorners: false),
            new ChartRenderCommand.Marker(new ChartMarkerRenderPlan([])),
            new ChartRenderCommand.Rectangle(
                new ChartPlanRect(5, 6, 7, 8),
                null,
                null,
                ChartRectangleRole.PlotArea),
        ];
        var sink = new RecordingSink();

        ChartRenderCommandDispatcher.Dispatch(commands, sink);

        sink.Commands.Should().Equal(commands);
    }

    [Fact]
    public void DispatchRejectsNullInputs()
    {
        var sink = new RecordingSink();

        var nullCommands = () => ChartRenderCommandDispatcher.Dispatch(null!, sink);
        var nullSink = () => ChartRenderCommandDispatcher.Dispatch([], null!);

        nullCommands.Should().Throw<ArgumentNullException>();
        nullSink.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MarkerDispatchPreservesPrimitiveOrderAndConcreteType()
    {
        ChartMarkerRenderPrimitive[] primitives =
        [
            new ChartMarkerRenderPrimitive.Rectangle(
                new ChartPlanRect(1, 2, 3, 4),
                null,
                null),
            new ChartMarkerRenderPrimitive.Line(
                new ChartPlanPoint(1, 2),
                new ChartPlanPoint(3, 4),
                new ChartStrokePlan(SrgbColor.Black, 255, 1)),
        ];
        var sink = new RecordingMarkerSink();

        ChartMarkerRenderPrimitiveDispatcher.Dispatch(primitives, sink);

        sink.Primitives.Should().Equal(primitives);
    }

    private sealed class RecordingSink : IChartRenderCommandSink
    {
        public List<ChartRenderCommand> Commands { get; } = [];

        public void Render(ChartRenderCommand.Frame command) => Commands.Add(command);
        public void Render(ChartRenderCommand.Rectangle command) => Commands.Add(command);
        public void Render(ChartRenderCommand.Line command) => Commands.Add(command);
        public void Render(ChartRenderCommand.Path command) => Commands.Add(command);
        public void Render(ChartRenderCommand.LinePath command) => Commands.Add(command);
        public void Render(ChartRenderCommand.Marker command) => Commands.Add(command);
        public void Render(ChartRenderCommand.PieSlice command) => Commands.Add(command);
        public void Render(ChartRenderCommand.DoughnutSlice command) => Commands.Add(command);
        public void Render(ChartRenderCommand.SurfaceFacet command) => Commands.Add(command);
        public void Render(ChartRenderCommand.Bubble command) => Commands.Add(command);
        public void Render(ChartRenderCommand.Text command) => Commands.Add(command);
    }

    private sealed class RecordingMarkerSink : IChartMarkerRenderPrimitiveSink
    {
        public List<ChartMarkerRenderPrimitive> Primitives { get; } = [];

        public void Render(ChartMarkerRenderPrimitive.Ellipse primitive) => Primitives.Add(primitive);
        public void Render(ChartMarkerRenderPrimitive.Rectangle primitive) => Primitives.Add(primitive);
        public void Render(ChartMarkerRenderPrimitive.Path primitive) => Primitives.Add(primitive);
        public void Render(ChartMarkerRenderPrimitive.Line primitive) => Primitives.Add(primitive);
    }
}
