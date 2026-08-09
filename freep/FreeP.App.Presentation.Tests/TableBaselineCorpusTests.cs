using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class TableBaselineCorpusTests
{
    [Fact]
    public void MediumStyle2BodyBands_UsePowerPointDark2CompatibilityColors()
    {
        var presentation = PptxPackageReader.Read(Path.Combine(FindCorpusDirectory(), "05-table.pptx"));
        var table = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Single(shape => shape.Kind == SlideShapeKind.Table)
            .Table!;

        var band2 = ((ShapeFill.Solid)table.ComputeEffectiveFill(1, 0, table.Rows[1].Cells[0])!).Color;
        var band1 = ((ShapeFill.Solid)table.ComputeEffectiveFill(2, 0, table.Rows[2].Cells[0])!).Color;

        band2.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Dk2);
        band2.SchemeColor.LumMod.Should().Be(1.0);
        band2.SchemeColor.LumOff.Should().Be(0.0);
        band2.SchemeColor.Tint.Should().BeApproximately(0.2, 0.000001);
        band2.Resolved.Should().Be(new SrgbColor(218, 221, 225));
        band1.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Dk2);
        band1.SchemeColor.Tint.Should().BeApproximately(0.1, 0.000001);
        band1.Resolved.Should().Be(new SrgbColor(236, 238, 240));
    }

    private static string FindCorpusDirectory() =>
        TestWorkspaceFileLocator.FindContainingDirectoryFromBaseDirectory(
            "tools", "FreeP.RenderCompare", "corpus", "05-table.pptx");
}
