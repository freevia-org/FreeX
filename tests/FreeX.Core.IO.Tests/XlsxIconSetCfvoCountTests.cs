using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for G6: GetIconSetThresholds must emit EXACTLY icon-count cfvo elements.
/// OOXML CT_IconSet requires the count to match the icon style (3/4/5). A list longer than
/// icon-count caused Excel to repair or strip the rule.
/// </summary>
public sealed class XlsxIconSetCfvoCountTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";

    private static MemoryStream SaveWithIconSetThresholds(
        string iconSetStyle,
        IReadOnlyList<CfThresholdModel> thresholds)
    {
        var wb = new Workbook("G6Test");
        var sheet = wb.AddSheet("Sheet1");
        var sheetId = sheet.Id;

        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheetId, row, 1), new NumberValue(row));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = iconSetStyle,
        };
        cf.IconSetThresholds.AddRange(thresholds);
        sheet.ConditionalFormats.Add(cf);

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Save_IconSetWith3StyleBut5Thresholds_EmitsExactly3Cfvo()
    {
        var thresholds = new List<CfThresholdModel>
        {
            new(CfThresholdType.Percent, "0"),
            new(CfThresholdType.Percent, "20"),
            new(CfThresholdType.Percent, "40"),
            new(CfThresholdType.Percent, "60"),   // excess
            new(CfThresholdType.Percent, "80"),   // excess
        };

        using var stream = SaveWithIconSetThresholds("3Arrows", thresholds);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(WorksheetPath)!;
        XDocument doc;
        using (var xmlStream = entry.Open())
            doc = XDocument.Load(xmlStream);

        var cfvoCount = doc.Root!
            .Elements(Ns + "conditionalFormatting")
            .Elements(Ns + "cfRule")
            .Elements(Ns + "iconSet")
            .SelectMany(e => e.Elements(Ns + "cfvo"))
            .Count();

        cfvoCount.Should().Be(3,
            "a 3-icon style must emit exactly 3 cfvo regardless of how many thresholds the model holds");
    }

    [Fact]
    public void Save_IconSetWith4StyleBut2Thresholds_PadsTo4Cfvo()
    {
        var thresholds = new List<CfThresholdModel>
        {
            new(CfThresholdType.Percent, "0"),
            new(CfThresholdType.Percent, "25"),
        };

        using var stream = SaveWithIconSetThresholds("4Arrows", thresholds);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(WorksheetPath)!;
        XDocument doc;
        using (var xmlStream = entry.Open())
            doc = XDocument.Load(xmlStream);

        var cfvoCount = doc.Root!
            .Elements(Ns + "conditionalFormatting")
            .Elements(Ns + "cfRule")
            .Elements(Ns + "iconSet")
            .SelectMany(e => e.Elements(Ns + "cfvo"))
            .Count();

        cfvoCount.Should().Be(4,
            "a 4-icon style must pad to exactly 4 cfvo when fewer thresholds are supplied");
    }

    [Fact]
    public void Save_IconSetWith5StyleAnd5Thresholds_Emits5Cfvo()
    {
        var thresholds = new List<CfThresholdModel>
        {
            new(CfThresholdType.Percent, "0"),
            new(CfThresholdType.Percent, "20"),
            new(CfThresholdType.Percent, "40"),
            new(CfThresholdType.Percent, "60"),
            new(CfThresholdType.Percent, "80"),
        };

        using var stream = SaveWithIconSetThresholds("5Arrows", thresholds);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(WorksheetPath)!;
        XDocument doc;
        using (var xmlStream = entry.Open())
            doc = XDocument.Load(xmlStream);

        var cfvoCount = doc.Root!
            .Elements(Ns + "conditionalFormatting")
            .Elements(Ns + "cfRule")
            .Elements(Ns + "iconSet")
            .SelectMany(e => e.Elements(Ns + "cfvo"))
            .Count();

        cfvoCount.Should().Be(5,
            "an exact match between threshold count and icon count must be preserved");
    }
}
