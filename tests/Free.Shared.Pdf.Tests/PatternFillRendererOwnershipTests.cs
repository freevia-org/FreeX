using FluentAssertions;
using Free.Shared.Drawing;

namespace Free.Shared.Pdf.Tests;

public sealed class PatternFillRendererOwnershipTests
{
    [Fact]
    public void LiveAndPdfRenderers_ConsumeSharedRecipesWithoutPresetBucketing()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs")),
            File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs")),
            File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Pdf", "PortablePdfWriter.cs")),
            File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Pdf.Skia", "SkiaPdfWriter.cs")),
        };

        sources[0].Should().Contain("DrawingMlPatternFillPlanner.Plan(fill.PatternPreset)");
        sources[1].Should().Contain("DrawingMlPatternFillPlanner.Plan(fill.PatternPreset)");
        sources[2].Should().Contain("pattern.Recipe.Primitives");
        sources[3].Should().Contain("pattern.Recipe.Primitives");

        foreach (var source in sources)
        {
            source.Should().Contain("DrawingMlPatternFillLine");
            source.Should().Contain("DrawingMlPatternFillEllipse");
            source.Should().NotContain("\"horz\" or \"ltHorz\"");
            source.Should().NotContain("case PdfPatternKind.Horizontal");
        }
    }

    [Fact]
    public void PdfPatternAdapter_UsesSharedFamilyAndRecipeDimensions()
    {
        var dot = PdfPatternFill.FromPreset("dotGrid", PdfColor.Black, new PdfColor(255, 255, 255));
        var brick = PdfPatternFill.FromPreset("horzBrick", PdfColor.Black, new PdfColor(255, 255, 255), 2);

        dot.Recipe.Family.Should().Be(DrawingMlPatternFillFamily.Dot);
        dot.StrokeWidth.Should().Be(1);
        brick.Kind.Should().Be(PdfPatternKind.Brick);
        brick.TileWidth.Should().Be(24);
        brick.TileHeight.Should().Be(16);
        brick.StrokeWidth.Should().Be(1);
        brick.Recipe.Primitives.Should().HaveCount(7);
    }
}
