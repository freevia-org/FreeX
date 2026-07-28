using System.Text;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowInkRenderPlannerTests
{
    [Fact]
    public void GeneratedInk_IsRenderedAsAbsoluteSlideSpaceStroke()
    {
        var presentation = Presentation.CreateEmpty();
        var state = SlideShowInkExecutionPlanner.CreateState(
            committedStrokes: new[]
            {
                new SlideShowInkStroke(
                    "generated",
                    0,
                    SlideShowPresenterPointerMode.Pen,
                    new SlideShowInkState("#336699", 5, 0.75),
                    new[] { new SlideShowInkPoint(10, 20), new SlideShowInkPoint(30, 40) }),
            });
        SlideShowInkPersistencePlanner.ApplyRetentionOnExit(presentation, state);
        var shape = presentation.Slides[0].Shapes.Single(item => item.Kind == SlideShapeKind.Ink);

        var strokes = SlideShowInkRenderPlanner.Build(shape, presentation);

        strokes.Should().ContainSingle();
        strokes[0].Points[0].X.Should().BeApproximately(10, 0.03);
        strokes[0].Points[0].Y.Should().BeApproximately(20, 0.03);
        strokes[0].Points[1].X.Should().BeApproximately(30, 0.03);
        strokes[0].Points[1].Y.Should().BeApproximately(40, 0.03);
        strokes[0].Color.Should().Be(new SrgbColor(0x33, 0x66, 0x99));
        strokes[0].ThicknessDip.Should().BeApproximately(5, 0.001);
        strokes[0].Alpha.Should().Be(191);

        var ops = SlideCompositor.Compose(presentation, presentation.Slides[0]);
        var rendered = ops.OfType<DrawOp.Shape>().Where(item => item.ShapeId == shape.Id).Should().ContainSingle().Subject;
        rendered.Geometry.Contours.Should().ContainSingle();
        rendered.Outline.Should().BeOfType<ResolvedOutline.Visible>();
    }

    [Fact]
    public void NativeInk_ConvertsUnitsAndFrameLocalCoordinates()
    {
        var presentation = Presentation.CreateEmpty();
        var info = new PreservedObjectInfo { ObjectKind = PreservedObjectKind.Ink };
        info.Parts["ppt/ink/native.xml"] = Encoding.UTF8.GetBytes(
            """
            <ink xmlns="http://www.w3.org/2003/InkML">
              <definitions>
                <context xml:id="ctx">
                  <traceFormat>
                    <channel name="X" units="mm" />
                    <channel name="Y" units="mm" />
                  </traceFormat>
                </context>
                <brush xml:id="brush">
                  <brushProperty name="color" value="#123456" />
                  <brushProperty name="width" value="1" units="mm" />
                  <brushProperty name="transparency" value="64" />
                </brush>
              </definitions>
              <trace contextRef="#ctx" brushRef="#brush">1 2, 10 2</trace>
            </ink>
            """);
        info.PartContentTypes["ppt/ink/native.xml"] = "application/inkml+xml";
        var shape = new SlideShape
        {
            Id = 27,
            Kind = SlideShapeKind.Ink,
            OffsetXEmu = 2 * 9525,
            OffsetYEmu = 3 * 9525,
            ExtentCxEmu = 100 * 9525,
            ExtentCyEmu = 50 * 9525,
            PreservedObject = info,
        };

        var strokes = SlideShowInkRenderPlanner.Build(shape, presentation);

        strokes.Should().ContainSingle();
        strokes[0].Points[0].X.Should().BeApproximately(2 + 96 / 25.4, 0.001);
        strokes[0].Points[0].Y.Should().BeApproximately(3 + 2 * 96 / 25.4, 0.001);
        strokes[0].ThicknessDip.Should().BeApproximately(96 / 25.4, 0.001);
        strokes[0].Alpha.Should().Be(191);
        strokes[0].Color.Should().Be(new SrgbColor(0x12, 0x34, 0x56));
    }
}
