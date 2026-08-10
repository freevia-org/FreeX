using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class ColorInputParserOwnershipSourceGuardTests
{
    [Fact]
    public void SpecializedParsersDelegateRgbTextOwnershipToColorInputParser()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var canonical = ReadSource(repoRoot, "ColorInputParser.cs");
        var conditional = ReadSource(
            repoRoot,
            "ConditionalFormatting",
            "ConditionalFormatInputParser.cs");
        var drawing = ReadSource(
            repoRoot,
            "DrawingInteraction",
            "DrawingInputParser.cs");

        canonical.Should().Contain("byte.TryParse(");
        canonical.Should().Contain("RgbTripletTextProfile.ConditionalFormatting");
        canonical.Should().Contain("RgbTripletTextProfile.DrawingInteraction");

        conditional.Should().Contain("ColorInputParser.FormatRgbColor(color)");
        conditional.Should().Contain("RgbTripletTextProfile.ConditionalFormatting");
        conditional.Should().NotContain("byte.TryParse(");
        conditional.Should().NotContain("new RgbColor(");
        conditional.Should().NotContain("NumberStyles.HexNumber");

        drawing.Should().Contain("RgbTripletTextProfile.DrawingInteraction");
        drawing.Should().NotContain("byte.TryParse(");
        drawing.Should().NotContain("new CellColor(");
        drawing.Should().NotContain("NumberStyles.HexNumber");
    }

    private static string ReadSource(string repoRoot, params string[] parts) =>
        File.ReadAllText(Path.Combine(
            [repoRoot, "src", "FreeX.App.Presentation", .. parts]));
}
