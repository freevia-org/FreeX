using System.IO.Compression;
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

        var colorScale = ReadWorksheetXml(saved)
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
    }

    [Fact]
    public void RoundTrip_ColorScaleThemeColors_WritesResolvedRgbColors()
    {
        using var source = CreateXlsxWithThemeColorScaleColors();
        var workbook = new XlsxFileAdapter().Load(source);
        using var saved = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, saved);

        var colors = ReadWorksheetXml(saved)
            .Descendants(MainNs + "colorScale")
            .Should()
            .ContainSingle()
            .Subject
            .Elements(MainNs + "color")
            .ToArray();
        colors.Should().HaveCount(3);
        colors[0].Attribute("rgb")?.Value.Should().Be(ToArgb(WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent1)));
        colors[1].Attribute("rgb")?.Value.Should().Be(ToArgb(WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent2)));
        colors[2].Attribute("rgb")?.Value.Should().Be(ToArgb(WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent3)));
    }

    private static MemoryStream CreateXlsxWithColorScaleGteThresholds()
    {
        return CreateXlsxWithPatchedWorksheet(root =>
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
        return CreateXlsxWithPatchedWorksheet(root =>
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

    private static MemoryStream CreateXlsxWithPatchedWorksheet(Action<XElement> patchRoot)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        using var package = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, package);
        package.Position = 0;

        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument xml;
            using (var reader = new StreamReader(entry.Open()))
                xml = XDocument.Load(reader);

            patchRoot(xml.Root!);

            entry.Delete();
            var replacement = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using var writer = new StreamWriter(replacement.Open());
            xml.Save(writer);
        }

        package.Position = 0;
        return new MemoryStream(package.ToArray());
    }

    private static XDocument ReadWorksheetXml(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        return XDocument.Load(reader);
    }

    private static string ToArgb(CellColor color) =>
        $"FF{color.R:X2}{color.G:X2}{color.B:X2}";

    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
}
