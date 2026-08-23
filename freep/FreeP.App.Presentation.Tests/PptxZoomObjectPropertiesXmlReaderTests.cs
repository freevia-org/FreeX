using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class PptxZoomObjectPropertiesXmlReaderTests
{
    [Theory]
    [InlineData("solid", OutlineDash.Solid)]
    [InlineData(" DASH ", OutlineDash.Dash)]
    [InlineData("dot", OutlineDash.Dot)]
    [InlineData("dashDot", OutlineDash.DashDot)]
    [InlineData("lgDash", OutlineDash.LongDash)]
    [InlineData("lgDashDot", OutlineDash.LongDashDot)]
    [InlineData("lgDashDotDot", OutlineDash.LongDashDotDot)]
    [InlineData("sysDash", OutlineDash.SystemDash)]
    [InlineData("sysDot", OutlineDash.SystemDot)]
    [InlineData("sysDashDot", OutlineDash.SystemDashDot)]
    public void ParseDashToken_maps_every_supported_DrawingML_preset(
        string token,
        OutlineDash expected)
    {
        PptxZoomObjectPropertiesXmlReader.ParseDashToken(token).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("customDash")]
    public void ParseDashToken_returns_null_for_absent_or_unsupported_tokens(string? token)
    {
        PptxZoomObjectPropertiesXmlReader.ParseDashToken(token).Should().BeNull();
    }

    [Fact]
    public void Read_projects_attributes_line_fills_geometry_and_effect_units()
    {
        var properties = XElement.Parse(
            """
            <p:zmPr xmlns:p="urn:p" xmlns:a="urn:a"
                    returnToParent="true" imageType="cover" transitionDur="1250" showBg="0">
              <a:srcRect l="1000" t="2000" r="3000" b="4000" />
              <p:spPr>
                <a:prstGeom prst="roundRect" />
                <a:ln w="25400">
                  <a:solidFill><a:srgbClr val="#a1b2c3" /></a:solidFill>
                  <a:prstDash val="lgDashDotDot" />
                  <a:gradFill>
                    <a:gsLst>
                      <a:gs pos="0"><a:srgbClr val="112233" /></a:gs>
                      <a:gs pos="100000"><a:srgbClr val="DDEEFF" /></a:gs>
                    </a:gsLst>
                    <a:lin ang="5400000" />
                  </a:gradFill>
                  <a:pattFill prst="PCT50">
                    <a:fgClr><a:srgbClr val="445566" /></a:fgClr>
                    <a:bgClr><a:srgbClr val="778899" /></a:bgClr>
                  </a:pattFill>
                  <a:noFill />
                </a:ln>
                <a:effectLst>
                  <a:outerShdw blurRad="50800" dist="38100" dir="2700000">
                    <a:srgbClr val="404040"><a:alpha val="42000" /></a:srgbClr>
                  </a:outerShdw>
                  <a:glow rad="152400">
                    <a:srgbClr val="00AAFF"><a:alpha val="37000" /></a:srgbClr>
                  </a:glow>
                  <a:softEdge rad="127000" />
                  <a:reflection stA="41000" blurRad="31750" dist="44450"
                                dir="5400000" sy="-75000" endPos="37500" />
                </a:effectLst>
              </p:spPr>
            </p:zmPr>
            """);

        var result = PptxZoomObjectPropertiesXmlReader.Read(properties);

        result.Should().NotBeNull();
        result!.ReturnToParent.Should().BeTrue();
        result.ImageType.Should().Be("cover");
        result.TransitionDuration.Should().Be("1250");
        result.ShowBackground.Should().BeFalse();
        (result.CropLeft, result.CropTop, result.CropRight, result.CropBottom)
            .Should().Be((1000, 2000, 3000, 4000));
        result.FrameBorderColor.Should().Be("A1B2C3");
        result.FrameBorderWidthEmu.Should().Be(25400);
        result.FrameBorderDash.Should().Be(OutlineDash.LongDashDotDot);
        result.FrameGeometry.Should().Be("roundRect");
        result.FrameBorderGradient.Should().Be(
            new ZoomFrameBorderGradient("112233", "DDEEFF", 5400000));
        result.FrameBorderPattern.Should().Be(
            new ZoomFrameBorderPattern("pct50", "445566", "778899"));
        result.FrameBorderNoFill.Should().BeTrue();
        result.FrameBorderShadow.Should().Be(
            new ZoomFrameBorderShadow("404040", 42000, 50800, 38100, 2700000));
        result.FrameBorderShadowEnabled.Should().BeTrue();
        result.FrameBorderGlow.Should().Be(
            new ZoomFrameBorderGlow("00AAFF", 37000, 152400));
        result.FrameBorderGlowEnabled.Should().BeTrue();
        result.FrameBorderSoftEdge.Should().Be(new ZoomFrameBorderSoftEdge(127000));
        result.FrameBorderSoftEdgeEnabled.Should().BeTrue();
        result.FrameBorderReflection.Should().Be(
            new ZoomFrameBorderReflection(41000, 31750, 44450, 5400000, -75000, 37500));
        result.FrameBorderReflectionEnabled.Should().BeTrue();
    }

    [Fact]
    public void Read_preserves_effect_defaults_and_theme_color_mapping()
    {
        var properties = XElement.Parse(
            """
            <p:zmPr xmlns:p="urn:p" xmlns:a="urn:a">
              <p:spPr>
                <a:ln><a:solidFill><a:schemeClr val="accent3" /></a:solidFill></a:ln>
                <a:effectLst>
                  <a:outerShdw><a:srgbClr val="ABCDEF" /></a:outerShdw>
                  <a:glow><a:srgbClr val="123456" /></a:glow>
                  <a:reflection />
                </a:effectLst>
              </p:spPr>
            </p:zmPr>
            """);

        var result = PptxZoomObjectPropertiesXmlReader.Read(properties)!;

        result.FrameBorderThemeColor.Should().Be(ThemeColorSlot.Accent3);
        result.FrameBorderShadow.Should().Be(
            new ZoomFrameBorderShadow("ABCDEF", 50000, 0, 0, 0));
        result.FrameBorderGlow.Should().Be(new ZoomFrameBorderGlow("123456", 50000, 0));
        result.FrameBorderReflection.Should().Be(
            new ZoomFrameBorderReflection(50000, 0, 0, 5400000, -100000, 100000));
    }

    [Fact]
    public void Read_can_bound_geometry_for_the_dialog_without_changing_package_import()
    {
        var properties = XElement.Parse(
            "<p:zmPr xmlns:p=\"urn:p\" xmlns:a=\"urn:a\"><p:spPr>"
            + "<a:prstGeom prst=\"hexagon\" /></p:spPr></p:zmPr>");

        PptxZoomObjectPropertiesXmlReader.Read(properties)!.FrameGeometry.Should().Be("hexagon");
        PptxZoomObjectPropertiesXmlReader.ReadDialogProjection(properties).Should().BeNull();
    }

    [Fact]
    public void Package_and_dialog_boolean_profiles_keep_their_existing_fallback_rules()
    {
        var properties = XElement.Parse("<zmPr returnToParent=\"on\" showBg=\"invalid\" />");

        var packageProjection = PptxZoomObjectPropertiesXmlReader.Read(properties)!;
        packageProjection.ReturnToParent.Should().BeFalse();
        packageProjection.ShowBackground.Should().BeFalse();

        var dialogProjection = PptxZoomObjectPropertiesXmlReader.ReadDialogProjection(properties)!;
        dialogProjection.ReturnToParent.Should().BeTrue();
        dialogProjection.ShowBackground.Should().BeNull();
    }

    [Fact]
    public void EffectiveSummaryTile_uses_shared_projection_and_keeps_empty_tile_fallback()
    {
        var fallback = new ZoomObjectProperties(
            ReturnToParent: false,
            ImageType: "preview",
            FrameBorderDash: OutlineDash.Dot);
        var info = new PreservedObjectInfo
        {
            ZoomProperties = fallback,
            RawXml =
                """
                <p:graphicFrame xmlns:p="urn:p" xmlns:p14="urn:p14" xmlns:a="urn:a">
                  <p14:summaryZmObj sectionId="one"><p14:zmPr /></p14:summaryZmObj>
                  <p14:summaryZmObj sectionId="two">
                    <p14:zmPr returnToParent="1">
                      <p14:spPr><a:ln><a:prstDash val="sysDashDot" /></a:ln></p14:spPr>
                    </p14:zmPr>
                  </p14:summaryZmObj>
                </p:graphicFrame>
                """,
        };

        ZoomObjectPropertiesPlanner.EffectiveSummaryTile(info, "one").Should().Be(fallback);
        ZoomObjectPropertiesPlanner.EffectiveSummaryTile(info, "two").Should().Be(
            new ZoomObjectProperties(
                ReturnToParent: true,
                FrameBorderDash: OutlineDash.SystemDashDot));
    }
}
