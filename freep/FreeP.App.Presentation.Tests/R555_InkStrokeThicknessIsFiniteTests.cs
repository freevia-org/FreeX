using System.Text;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r555: ink annotations are stored in the .pptx as an InkML part and parsed back by
/// <see cref="SlideShowInkRenderPlanner.Build"/>, so every number in them is file-controlled.
/// The trace COORDINATES are guarded explicitly -- ReadTracePoints filters
/// <c>!IsNaN &amp;&amp; !IsInfinity</c> -- but the brush WIDTH ten lines above was not, and
/// <c>Math.Max(0.1, thickness)</c> is not a guard: it returns Infinity for Infinity and NaN for
/// NaN, because Math.Max propagates NaN.
///
/// <para>An infinite stroke width reaches the compositor as real geometry. The remedy is the
/// planner's own existing contract: a brush property that cannot be used falls back to the
/// default brush, which is exactly what it already does when the attribute is absent or
/// unparseable.</para>
/// </summary>
public sealed class R555_InkStrokeThicknessIsFiniteTests
{
    private static SlideShape InkShapeWithWidth(string width)
    {
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
                  <brushProperty name="width" value="WIDTH" units="mm" />
                  <brushProperty name="transparency" value="64" />
                </brush>
              </definitions>
              <trace contextRef="#ctx" brushRef="#brush">1 2, 10 2</trace>
            </ink>
            """.Replace("WIDTH", width, StringComparison.Ordinal));
        info.PartContentTypes["ppt/ink/native.xml"] = "application/inkml+xml";

        return new SlideShape
        {
            Id = 27,
            Kind = SlideShapeKind.Ink,
            OffsetXEmu = 2 * 9525,
            OffsetYEmu = 3 * 9525,
            ExtentCxEmu = 100 * 9525,
            ExtentCyEmu = 50 * 9525,
            PreservedObject = info,
        };
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void A_brush_width_that_is_not_finite_falls_back_to_the_default(string hostile)
    {
        var presentation = Presentation.CreateEmpty();

        var strokes = SlideShowInkRenderPlanner.Build(InkShapeWithWidth(hostile), presentation);

        strokes.Should().ContainSingle();
        double.IsFinite(strokes[0].ThicknessDip).Should().BeTrue(
            "ThicknessDip was " + strokes[0].ThicknessDip);
    }

    [Fact]
    public void An_ordinary_brush_width_is_still_converted()
    {
        // Non-vacuity: a guard that rejected every width would satisfy the finiteness assertion
        // above while silently flattening every real stroke to the default thickness. 1mm is
        // 96/25.4 DIP, the same conversion the existing native-ink test pins.
        var presentation = Presentation.CreateEmpty();

        var strokes = SlideShowInkRenderPlanner.Build(InkShapeWithWidth("1"), presentation);

        strokes.Should().ContainSingle();
        strokes[0].ThicknessDip.Should().BeApproximately(96 / 25.4, 0.001);
    }

    [Fact]
    public void Trace_coordinates_were_already_guarded_and_stay_guarded()
    {
        // Pins the sibling path that WAS correct, so a later change cannot remove the filter that
        // made this defect thickness-only rather than geometry-wide.
        var presentation = Presentation.CreateEmpty();
        var shape = InkShapeWithWidth("1");
        shape.PreservedObject!.Parts["ppt/ink/native.xml"] = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(shape.PreservedObject.Parts["ppt/ink/native.xml"])
                .Replace(">1 2, 10 2<", ">1 2, 1e400 2<", StringComparison.Ordinal));

        var strokes = SlideShowInkRenderPlanner.Build(shape, presentation);

        strokes.SelectMany(stroke => stroke.Points)
            .Should().OnlyContain(point => double.IsFinite(point.X) && double.IsFinite(point.Y));
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("NaN")]
    public void A_freep_thicknessDip_attribute_that_is_not_finite_falls_back(string hostile)
    {
        // The SECOND parse helper. FreeP writes its own thicknessDip/opacity attributes onto the
        // trace when it persists generated ink, and those go through ParseOptionalDouble rather
        // than the InkML brushProperty path above. Neutering one guard left the other's tests
        // green, which is how I found that this path needed its own case rather than assuming one
        // fix covered both.
        var presentation = Presentation.CreateEmpty();
        var shape = InkShapeWithWidth("1");
        shape.PreservedObject!.Parts["ppt/ink/native.xml"] = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(shape.PreservedObject.Parts["ppt/ink/native.xml"])
                .Replace(
                    "<trace contextRef=\"#ctx\" brushRef=\"#brush\">",
                    "<trace xmlns:fp=\"https://freex.local/freep/ink/2026\" contextRef=\"#ctx\""
                        + " brushRef=\"#brush\" fp:thicknessDip=\"" + hostile + "\">",
                    StringComparison.Ordinal));

        var strokes = SlideShowInkRenderPlanner.Build(shape, presentation);

        strokes.Should().ContainSingle();
        double.IsFinite(strokes[0].ThicknessDip).Should().BeTrue(
            "ThicknessDip was " + strokes[0].ThicknessDip);
    }
}
