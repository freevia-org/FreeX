using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

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

    private static MemoryStream CreateXlsxWithColorScaleGteThresholds()
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

            xml.Root!.Add(
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

    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
}
