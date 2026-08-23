using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class SharedDrawingPolicyTests
{
    private static readonly XNamespace DrawingNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void TryPatchNativeFontScheme_ChangesOnlyLatinTypefaces()
    {
        const string source = """
            <a:fontScheme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Custom">
              <a:majorFont>
                <a:latin typeface="Old Major" panose="020B0604020202020204"/>
                <a:ea typeface="Yu Gothic"/>
                <a:cs typeface="Times New Roman"/>
                <a:font script="Jpan" typeface="Yu Gothic"/>
              </a:majorFont>
              <a:minorFont>
                <a:latin typeface="Old Minor"/>
                <a:ea typeface="Yu Mincho"/>
                <a:cs typeface="Arial"/>
              </a:minorFont>
              <a:extLst><a:ext uri="urn:preserve-me"/></a:extLst>
            </a:fontScheme>
            """;

        var patched = DrawingMlThemeXml.TryPatchNativeFontScheme(source, "New Major", "New Minor");

        patched.Should().NotBeNull();
        patched!.Attribute("name")!.Value.Should().Be("Custom");
        var major = patched.Element(DrawingNamespace + "majorFont")!;
        var minor = patched.Element(DrawingNamespace + "minorFont")!;
        major.Element(DrawingNamespace + "latin")!.Attribute("typeface")!.Value.Should().Be("New Major");
        major.Element(DrawingNamespace + "latin")!.Attribute("panose")!.Value.Should().Be("020B0604020202020204");
        minor.Element(DrawingNamespace + "latin")!.Attribute("typeface")!.Value.Should().Be("New Minor");
        major.Element(DrawingNamespace + "ea")!.Attribute("typeface")!.Value.Should().Be("Yu Gothic");
        major.Element(DrawingNamespace + "cs")!.Attribute("typeface")!.Value.Should().Be("Times New Roman");
        major.Element(DrawingNamespace + "font")!.Attribute("typeface")!.Value.Should().Be("Yu Gothic");
        minor.Element(DrawingNamespace + "ea")!.Attribute("typeface")!.Value.Should().Be("Yu Mincho");
        minor.Element(DrawingNamespace + "cs")!.Attribute("typeface")!.Value.Should().Be("Arial");
        patched.Element(DrawingNamespace + "extLst")!
            .Element(DrawingNamespace + "ext")!
            .Attribute("uri")!
            .Value.Should().Be("urn:preserve-me");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<a:fontScheme")]
    [InlineData("<fontScheme />")]
    [InlineData("<a:clrScheme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" />")]
    public void TryPatchNativeFontScheme_InvalidOrWrongRoot_ReturnsNull(string? source)
    {
        DrawingMlThemeXml.TryPatchNativeFontScheme(source, "Major", "Minor").Should().BeNull();
    }

    [Theory]
    [InlineData(1d, null, 2d)]
    [InlineData(50d, null, 9d)]
    [InlineData(100d, null, 18d)]
    [InlineData(1000d, null, 18d)]
    [InlineData(200d, -1d, 0d)]
    [InlineData(200d, 0d, 0d)]
    [InlineData(200d, 25000d, 50d)]
    [InlineData(200d, 50000d, 100d)]
    [InlineData(200d, 90000d, 100d)]
    public void RoundedRectangleCornerRadius_PreservesFallbackAndAuthoredClamping(
        double minimumDimension,
        double? adjustment,
        double expected)
    {
        PresetShapeAdjustmentMath
            .RoundedRectangleCornerRadius(minimumDimension, adjustment)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(-1000, 0.04)]
    [InlineData(0, 0.04)]
    [InlineData(4000, 0.04)]
    [InlineData(25000, 0.25)]
    [InlineData(45000, 0.45)]
    [InlineData(90000, 0.45)]
    public void RibbonBandTop_PreservesDrawingMlFloorAndCeiling(
        double adjustment,
        double expected)
    {
        PresetShapeAdjustmentMath.RibbonBandTop(adjustment).Should().Be(expected);
    }
}
