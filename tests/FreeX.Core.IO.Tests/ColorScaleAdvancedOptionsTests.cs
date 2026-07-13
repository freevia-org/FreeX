using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class ColorScaleAdvancedOptionsTests
{
    [Fact]
    public void Load_ColorScaleCfvoGteAttributes_MapsFirstClassProperties()
    {
        using var source = CreateXlsxWithColorScaleGteThresholds();

        var workbook = new XlsxFileAdapter().Load(source);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.RuleType.Should().Be(CfRuleType.ColorScale);
        rule.MinThresholdGreaterThanOrEqual.Should().BeFalse();
        rule.MidThresholdGreaterThanOrEqual.Should().BeTrue();
        rule.MaxThresholdGreaterThanOrEqual.Should().BeFalse();
    }

    [Fact]
    public void RoundTrip_ColorScaleCfvoGteAttributes_PreservesThresholdAttributesWithoutDuplication()
    {
        using var source = CreateXlsxWithColorScaleGteThresholds();
        var workbook = new XlsxFileAdapter().Load(source);
        using var saved = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, saved);

        var colorScale = XlsxPackageTestHelper.ReadWorksheetXml(saved)
            .Descendants(MainNs + "colorScale")
            .Should()
            .ContainSingle()
            .Subject;
        var thresholds = colorScale.Elements(MainNs + "cfvo").ToArray();
        thresholds.Should().HaveCount(3);
        thresholds[0].Attribute("gte")?.Value.Should().Be("0");
        thresholds[1].Attribute("gte")?.Value.Should().Be("1");
        thresholds[2].Attribute("gte")?.Value.Should().Be("0");
    }

    [Fact]
    public void Load_ColorScaleThemeColors_ResolvesWorkbookThemeColors()
    {
        using var source = CreateXlsxWithThemeColorScaleColors();

        var workbook = new XlsxFileAdapter().Load(source);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.RuleType.Should().Be(CfRuleType.ColorScale);
        rule.MinColor.Should().Be(RgbColor.FromCellColor(WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent1)));
        rule.MidColor.Should().Be(RgbColor.FromCellColor(WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent2)));
        rule.MaxColor.Should().Be(RgbColor.FromCellColor(WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent3)));
        // Source fields must be populated so the writer can round-trip theme attributes.
        rule.MinColorSource.Should().Be(new CfColorStopSource(4, 0), because: "Accent1 = OOXML theme index 4, no tint");
        rule.MidColorSource.Should().Be(new CfColorStopSource(5, 0), because: "Accent2 = OOXML theme index 5, no tint");
        rule.MaxColorSource.Should().Be(new CfColorStopSource(6, 0), because: "Accent3 = OOXML theme index 6, no tint");
    }

    [Fact]
    public void RoundTrip_ColorScaleThemeColors_WritesThemeIndexAttributes()
    {
        // When colorScale stop colors were expressed as theme references in the source file,
        // the writer must round-trip the original theme/tint attributes instead of
        // flattening to sRGB — so the saved XLSX stays theme-aware.
        using var source = CreateXlsxWithThemeColorScaleColors();
        var workbook = new XlsxFileAdapter().Load(source);
        using var saved = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, saved);

        var colors = XlsxPackageTestHelper.ReadWorksheetXml(saved)
            .Descendants(MainNs + "colorScale")
            .Should()
            .ContainSingle()
            .Subject
            .Elements(MainNs + "color")
            .ToArray();
        colors.Should().HaveCount(3);
        // The saved elements must carry theme= attributes (4, 5, 6) not rgb= ones.
        colors[0].Attribute("theme")?.Value.Should().Be("4", because: "Accent1 is OOXML theme index 4");
        colors[1].Attribute("theme")?.Value.Should().Be("5", because: "Accent2 is OOXML theme index 5");
        colors[2].Attribute("theme")?.Value.Should().Be("6", because: "Accent3 is OOXML theme index 6");
        colors[0].Attribute("rgb").Should().BeNull(because: "theme color must not be duplicated as rgb");
        colors[1].Attribute("rgb").Should().BeNull(because: "theme color must not be duplicated as rgb");
        colors[2].Attribute("rgb").Should().BeNull(because: "theme color must not be duplicated as rgb");
    }

    [Fact]
    public void Load_ColorScaleThemeColorWithTint_ResolvesCorrectRgbAndPreservesSource()
    {
        // A color with theme="4" tint="0.4" should resolve to the tinted Accent1 color
        // AND record the source so the writer can round-trip the tint attribute.
        using var source = CreateXlsxWithThemeColorScaleColorsWithTint();

        var workbook = new XlsxFileAdapter().Load(source);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.RuleType.Should().Be(CfRuleType.ColorScale);
        // The resolved color must be the tinted version (not the raw theme color).
        var expectedTinted = RgbColor.FromCellColor(WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.4));
        rule.MinColor.Should().Be(expectedTinted, because: "theme=4 tint=0.4 resolves to tinted Accent1");
        // Source must carry both the theme index and the tint.
        rule.MinColorSource.Should().Be(new CfColorStopSource(4, 0.4), because: "tint must be recorded for round-trip");
    }

    [Fact]
    public void RoundTrip_ColorScaleThemeColorWithTint_PreservesThemeIndexAndTint()
    {
        // When a colorScale stop has theme+tint, saving must preserve both attributes.
        using var source = CreateXlsxWithThemeColorScaleColorsWithTint();
        var workbook = new XlsxFileAdapter().Load(source);
        using var saved = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, saved);

        var colors = XlsxPackageTestHelper.ReadWorksheetXml(saved)
            .Descendants(MainNs + "colorScale")
            .Should()
            .ContainSingle()
            .Subject
            .Elements(MainNs + "color")
            .ToArray();
        colors.Should().HaveCount(2);
        // Min: theme=4 tint=0.4
        colors[0].Attribute("theme")?.Value.Should().Be("4");
        colors[0].Attribute("tint")?.Value.Should().NotBeNullOrEmpty(because: "tint must be written");
        double.Parse(colors[0].Attribute("tint")!.Value, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(0.4, 0.0001, because: "tint round-trips");
        colors[0].Attribute("rgb").Should().BeNull(because: "theme color must not be duplicated as rgb");
        // Max: theme=5, no tint
        colors[1].Attribute("theme")?.Value.Should().Be("5");
        colors[1].Attribute("tint").Should().BeNull(because: "no tint was specified");
    }

    [Fact]
    public void Load_ColorScaleIndexedColors_ResolvesWorkbookIndexedPaletteWithTint()
    {
        using var source = CreateXlsxWithIndexedColorScaleColors();

        var workbook = new XlsxFileAdapter().Load(source);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.RuleType.Should().Be(CfRuleType.ColorScale);
        rule.MinColor.Should().Be(new RgbColor(19, 120, 221));
        rule.MidColor.Should().Be(new RgbColor(10, 20, 30));
        rule.MaxColor.Should().Be(new RgbColor(69, 131, 193));
    }

    [Fact]
    public void RoundTrip_ColorScaleIndexedColors_WritesTintedResolvedRgbColors()
    {
        using var source = CreateXlsxWithIndexedColorScaleColors();
        var workbook = new XlsxFileAdapter().Load(source);
        using var saved = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, saved);

        var colors = XlsxPackageTestHelper.ReadWorksheetXml(saved)
            .Descendants(MainNs + "colorScale")
            .Should()
            .ContainSingle()
            .Subject
            .Elements(MainNs + "color")
            .ToArray();
        colors.Should().HaveCount(3);
        colors[0].Attribute("rgb")?.Value.Should().Be(ToArgb(new RgbColor(19, 120, 221)));
        colors[1].Attribute("rgb")?.Value.Should().Be(ToArgb(new RgbColor(10, 20, 30)));
        colors[2].Attribute("rgb")?.Value.Should().Be(ToArgb(new RgbColor(91, 131, 171)));
    }

    private static MemoryStream CreateXlsxWithColorScaleGteThresholds()
    {
        return XlsxPackageTestHelper.CreatePackageWithPatchedWorksheet(root =>
        {
            root.Add(
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "colorScale"),
                        new XAttribute("priority", "1"),
                        new XElement(MainNs + "colorScale",
                            new XElement(MainNs + "cfvo",
                                new XAttribute("type", "num"),
                                new XAttribute("val", "0"),
                                new XAttribute("gte", "0")),
                            new XElement(MainNs + "cfvo",
                                new XAttribute("type", "percentile"),
                                new XAttribute("val", "50"),
                                new XAttribute("gte", "1")),
                            new XElement(MainNs + "cfvo",
                                new XAttribute("type", "num"),
                                new XAttribute("val", "100"),
                                new XAttribute("gte", "0")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FF00AA00")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FFFFFF00")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FFAA0000"))))));
        });
    }

    private static MemoryStream CreateXlsxWithThemeColorScaleColors()
    {
        return XlsxPackageTestHelper.CreatePackageWithPatchedWorksheet(root =>
        {
            root.Add(
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "colorScale"),
                        new XAttribute("priority", "1"),
                        new XElement(MainNs + "colorScale",
                            new XElement(MainNs + "cfvo", new XAttribute("type", "num"), new XAttribute("val", "0")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "percentile"), new XAttribute("val", "50")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "num"), new XAttribute("val", "100")),
                            new XElement(MainNs + "color", new XAttribute("theme", "4")),
                            new XElement(MainNs + "color", new XAttribute("theme", "5")),
                            new XElement(MainNs + "color", new XAttribute("theme", "6"))))));
        });
    }

    /// <summary>
    /// 2-color scale: min = Accent1 (theme 4) with tint 0.4, max = Accent2 (theme 5) with no tint.
    /// </summary>
    private static MemoryStream CreateXlsxWithThemeColorScaleColorsWithTint()
    {
        return XlsxPackageTestHelper.CreatePackageWithPatchedWorksheet(root =>
        {
            root.Add(
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "colorScale"),
                        new XAttribute("priority", "1"),
                        new XElement(MainNs + "colorScale",
                            new XElement(MainNs + "cfvo", new XAttribute("type", "min")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "max")),
                            new XElement(MainNs + "color",
                                new XAttribute("theme", "4"),
                                new XAttribute("tint", "0.4")),
                            new XElement(MainNs + "color", new XAttribute("theme", "5"))))));
        });
    }

    private static MemoryStream CreateXlsxWithIndexedColorScaleColors()
    {
        using var sourcePackage = XlsxPackageTestHelper.CreatePackageWithPatchedWorksheet(root =>
        {
            root.Add(
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "colorScale"),
                        new XAttribute("priority", "1"),
                        new XElement(MainNs + "colorScale",
                            new XElement(MainNs + "cfvo", new XAttribute("type", "num"), new XAttribute("val", "0")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "percentile"), new XAttribute("val", "50")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "num"), new XAttribute("val", "100")),
                            new XElement(MainNs + "color", new XAttribute("indexed", "4"), new XAttribute("tint", "-0.25")),
                            new XElement(MainNs + "color", new XAttribute("indexed", "5")),
                            new XElement(MainNs + "color", new XAttribute("indexed", "6"), new XAttribute("tint", "0.2"))))));
        });
        var package = new MemoryStream();
        sourcePackage.Position = 0;
        sourcePackage.CopyTo(package);
        ReplaceIndexedColors(
            package,
            "FF000000",
            "FF000000",
            "FF000000",
            "FF000000",
            "FF50A0F0",
            "FF0A141E",
            "FF326496");
        return package;
    }

    private static void ReplaceIndexedColors(MemoryStream package, params string[] argbColors)
    {
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/styles.xml", xml =>
        {
            var colors = xml.Root!.Element(MainNs + "colors");
            if (colors is null)
            {
                colors = new XElement(MainNs + "colors");
                xml.Root!.Add(colors);
            }

            colors.Element(MainNs + "indexedColors")?.Remove();
            colors.Add(new XElement(
                MainNs + "indexedColors",
                argbColors.Select(color => new XElement(MainNs + "rgbColor", new XAttribute("rgb", color)))));
        });
    }

    private static string ToArgb(CellColor color) =>
        $"FF{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string ToArgb(RgbColor color) =>
        $"FF{color.R:X2}{color.G:X2}{color.B:X2}";

    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
}
