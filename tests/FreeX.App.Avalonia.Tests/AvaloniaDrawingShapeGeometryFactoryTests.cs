using Avalonia.Headless;
using FreeX.App.Avalonia;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaDrawingShapeGeometryFactoryTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static Task RunOnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    [Fact]
    public Task CreateGeometry_ReturnsSharedBuilderGeometryForAutoShapes() => RunOnUiThread(() =>
    {
        var geometry = AvaloniaDrawingShapeGeometryFactory.CreateGeometry(DrawingShapeKind.RightArrow, 120, 80);

        geometry.Should().NotBeNull();
        geometry!.Bounds.Width.Should().BeGreaterThan(0);
        geometry.Bounds.Height.Should().BeGreaterThan(0);
    });

    [Theory]
    [InlineData(DrawingShapeKind.Rectangle)]
    [InlineData(DrawingShapeKind.Ellipse)]
    public Task CreateGeometry_KeepsDedicatedPrimitivePath(DrawingShapeKind kind) => RunOnUiThread(() =>
    {
        AvaloniaDrawingShapeGeometryFactory.CreateGeometry(kind, 120, 80).Should().BeNull();
    });
}
