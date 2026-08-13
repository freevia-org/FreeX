using Avalonia;
using Avalonia.Media;
using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class SvgIconRasterizerTests
{
    [Fact]
    public void Painted_bounds_load_omits_viewbox_backing_without_changing_default_load()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("free-shared-ribbon-svg-");
        var path = Path.Combine(temporaryDirectory.Path, "icon.svg");
        File.WriteAllText(
            path,
            """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
              <line x1="16" y1="4" x2="16" y2="28" stroke="#000000" stroke-width="2" />
            </svg>
            """);

        {
            var defaultGroup = SvgIconRasterizer.LoadFile(path).Drawing
                .Should().BeOfType<DrawingGroup>().Subject;
            defaultGroup.Children.Should().HaveCount(2);
            var backing = defaultGroup.Children[0].Should().BeOfType<GeometryDrawing>().Subject;
            backing.Brush.Should().Be(Brushes.Transparent);
            backing.Geometry.Should().BeOfType<RectangleGeometry>()
                .Which.Rect.Should().Be(new Rect(0, 0, 32, 32));
            defaultGroup.Children[1].Should().BeOfType<GeometryDrawing>()
                .Which.Geometry.Should().BeOfType<LineGeometry>();

            var paintedGroup = SvgIconRasterizer.LoadFileToPaintedBounds(path).Drawing
                .Should().BeOfType<DrawingGroup>().Subject;
            paintedGroup.Children.Should().ContainSingle();
            var paintedLine = paintedGroup.Children[0].Should().BeOfType<GeometryDrawing>().Subject;
            paintedLine.Pen.Should().NotBeNull();
            paintedLine.Geometry.Should().BeOfType<LineGeometry>();
        }
    }
}
