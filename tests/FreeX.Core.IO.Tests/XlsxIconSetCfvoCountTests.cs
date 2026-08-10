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

    /// <summary>
    /// round133-remediation-io-cf-iconset-legacy-fallback: an x14-only icon-set style whose icon
    /// count is not 3 (e.g. "5Boxes", 5 icons) has no EXACT 3-icon legacy analogue. The legacy
    /// fallback style is always the fixed 3-icon "3TrafficLights1", so writing the real style's 5
    /// thresholds into that element straight would produce a &lt;iconSet&gt; with 5 &lt;cfvo&gt;
    /// children under a 3-icon type -- schema-mismatched OOXML that Excel repairs/strips on open.
    /// Omitting the legacy block entirely (the original round-133 fix) is worse: a reader that only
    /// understands the classic &lt;cfRule&gt; block then sees NO rule at all. Instead the writer must
    /// downsample the real thresholds to a valid 3-icon approximation (evenly-spaced index
    /// selection: first, middle, last of the 5) and still write the legacy block.
    /// </summary>
    [Fact]
    public void Save_IconSetX14OnlyStyleWithMismatchedIconCount_WritesDownsampledLegacyIconSetBlock()
    {
        using var stream = SaveWithIconSetThresholds("5Boxes",
        [
            new(CfThresholdType.Percent, "0"),
            new(CfThresholdType.Percent, "20"),
            new(CfThresholdType.Percent, "40"),
            new(CfThresholdType.Percent, "60"),
            new(CfThresholdType.Percent, "80"),
        ]);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(WorksheetPath)!;
        XDocument doc;
        using (var xmlStream = entry.Open())
            doc = XDocument.Load(xmlStream);

        var legacyIconSet = doc.Root!
            .Elements(Ns + "conditionalFormatting")
            .Elements(Ns + "cfRule")
            .Elements(Ns + "iconSet")
            .Should().ContainSingle(
                "a legacy-only reader must still see a usable icon-set rule, not nothing, even when " +
                "the real style has no exact 3-icon legacy analogue")
            .Subject;

        legacyIconSet.Attribute("iconSet")!.Value.Should().Be("3TrafficLights1");
        var cfvoValues = legacyIconSet.Elements(Ns + "cfvo")
            .Select(e => e.Attribute("val")?.Value)
            .ToList();
        cfvoValues.Should().HaveCount(3, "the legacy cfvo count must match the 3-icon legacy fallback style");
        // Evenly-spaced downsample of [0,20,40,60,80] to 3 picks indices 0,2,4 -> [0,40,80]: the
        // approximation must be derived from the REAL thresholds, not synthesized generic defaults.
        cfvoValues.Should().Equal(["0", "40", "80"],
            "the 3-icon approximation must be an evenly-spaced downsample of the real 5-icon thresholds");
    }

    /// <summary>
    /// Sibling/no-regression case for round133-io-cf-iconset-legacy-cfvo-count: an x14-only style
    /// whose icon count DOES match the 3-icon legacy fallback (e.g. "3Stars") must keep writing the
    /// legacy compatibility block, with exactly 3 cfvo -- the new guard must not over-correct and
    /// start omitting it too.
    /// </summary>
    [Fact]
    public void Save_IconSetX14OnlyStyleWithMatchingIconCount_KeepsLegacyIconSetBlockNoRegression()
    {
        using var stream = SaveWithIconSetThresholds("3Stars",
        [
            new(CfThresholdType.Percent, "0"),
            new(CfThresholdType.Percent, "33"),
            new(CfThresholdType.Percent, "67"),
        ]);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(WorksheetPath)!;
        XDocument doc;
        using (var xmlStream = entry.Open())
            doc = XDocument.Load(xmlStream);

        var legacyIconSet = doc.Root!
            .Elements(Ns + "conditionalFormatting")
            .Elements(Ns + "cfRule")
            .Elements(Ns + "iconSet")
            .Should().ContainSingle("a 3-icon x14-only style still has a valid 3-icon legacy analogue")
            .Subject;

        legacyIconSet.Attribute("iconSet")!.Value.Should().Be("3TrafficLights1");
        legacyIconSet.Elements(Ns + "cfvo").Should().HaveCount(3);
    }
}
