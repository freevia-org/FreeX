using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for R70-io-cf-databar-cfvo-6-3 (deferred since round 61): the x14 extended
/// data-bar cfvo @type is the ONLY place OOXML distinguishes an EXPLICIT Lowest/Highest Value
/// endpoint (<see cref="CfThresholdType.Min"/>/<see cref="CfThresholdType.Max"/>, written as x14 cfvo
/// type="min"/"max") from Excel's "Automatic" endpoint (<see cref="CfThresholdType.AutoMin"/>/
/// <see cref="CfThresholdType.AutoMax"/>, written as x14 cfvo type="autoMin"/"autoMax") -- the classic
/// (pre-2010-compatible) cfvo block always writes "min"/"max" for BOTH cases. Before this fix the
/// model had no Auto* variants and the reader never even looked at the x14 cfvo @type at all, so an
/// explicit choice silently round-tripped back as (and rendered as) Automatic.
/// </summary>
public sealed class R70_DataBarExplicitAutoCfvoRoundTripTests
{
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    private static ConditionalFormat AddDataBar(
        Sheet sheet,
        CfThresholdType minType,
        CfThresholdType maxType,
        bool border = false)
    {
        var rule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
            DataBarMinThresholdType = minType,
            DataBarMaxThresholdType = maxType,
            DataBarBorder = border,
        };
        sheet.ConditionalFormats.Add(rule);
        return rule;
    }

    [Fact]
    public void Save_ExplicitLowestHighestValue_WritesX14CfvoWithExplicitMinMaxType_NotAutomatic()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        // Explicit Lowest/Highest Value alone (no border/axis/gradient reason) must still trigger
        // generation of the x14 extended block -- it is the only place this choice survives a save.
        AddDataBar(sheet, CfThresholdType.Min, CfThresholdType.Max);
        using var stream = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, stream);

        var x14DataBar = XlsxPackageTestHelper.ReadWorksheetXml(stream).Descendants(X14Ns + "dataBar")
            .Should().ContainSingle("an explicit Lowest/Highest Value endpoint must generate the x14 extended block").Subject;
        var thresholds = x14DataBar.Elements(X14Ns + "cfvo").ToArray();
        thresholds.Should().HaveCount(2);
        thresholds[0].Attribute("type")!.Value.Should().Be("min");
        thresholds[1].Attribute("type")!.Value.Should().Be("max");
    }

    [Fact]
    public void Save_Automatic_WritesX14CfvoWithAutoMinAutoMaxType()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        // Border forces x14 generation for an otherwise-default (Automatic) data bar, so the cfvo
        // @type this produces can be compared directly against the explicit case above.
        AddDataBar(sheet, CfThresholdType.AutoMin, CfThresholdType.AutoMax, border: true);
        using var stream = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, stream);

        var x14DataBar = XlsxPackageTestHelper.ReadWorksheetXml(stream).Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
        var thresholds = x14DataBar.Elements(X14Ns + "cfvo").ToArray();
        thresholds.Should().HaveCount(2);
        thresholds[0].Attribute("type")!.Value.Should().Be("autoMin");
        thresholds[1].Attribute("type")!.Value.Should().Be("autoMax");
    }

    [Fact]
    public void RoundTrip_ExplicitLowestHighestValue_SurvivesLoadAsExplicitNotAutomatic()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        AddDataBar(sheet, CfThresholdType.Min, CfThresholdType.Max);
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(stream);

        var rule = reloaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarMinThresholdType.Should().Be(CfThresholdType.Min,
            "an explicit Lowest Value endpoint must round-trip distinctly from Automatic, not collapse to AutoMin");
        rule.DataBarMaxThresholdType.Should().Be(CfThresholdType.Max,
            "an explicit Highest Value endpoint must round-trip distinctly from Automatic, not collapse to AutoMax");
    }

    [Fact]
    public void RoundTrip_Automatic_SurvivesLoadAsAutomaticNotExplicit()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        AddDataBar(sheet, CfThresholdType.AutoMin, CfThresholdType.AutoMax, border: true);
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(stream);

        var rule = reloaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarMinThresholdType.Should().Be(CfThresholdType.AutoMin);
        rule.DataBarMaxThresholdType.Should().Be(CfThresholdType.AutoMax);
    }

    [Fact]
    public void Load_ClassicOnlyDataBar_WithNoX14Block_DefaultsToAutomatic()
    {
        // A classic-only (pre-2010, no x14 extended block) data bar has no way to express an explicit
        // Lowest/Highest Value distinctly -- Excel 2007 data bars are always "Automatic" -- so a bare
        // classic cfvo type="min"/"max" with no x14 override must default to AutoMin/AutoMax, not the
        // explicit Min/Max.
        using var package = XlsxPackageTestHelper.CreateSingleCellWorkbookPackage();
        XNamespace mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XlsxPackageTestHelper.PatchWorksheetXml(package, xml =>
        {
            xml.Root!.Add(
                new XElement(mainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(mainNs + "cfRule",
                        new XAttribute("type", "dataBar"),
                        new XAttribute("priority", "1"),
                        new XElement(mainNs + "dataBar",
                            new XElement(mainNs + "cfvo", new XAttribute("type", "min")),
                            new XElement(mainNs + "cfvo", new XAttribute("type", "max")),
                            new XElement(mainNs + "color", new XAttribute("rgb", "FF0A141E"))))));
        });

        var workbook = new XlsxFileAdapter().Load(package);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarMinThresholdType.Should().Be(CfThresholdType.AutoMin);
        rule.DataBarMaxThresholdType.Should().Be(CfThresholdType.AutoMax);
    }
}
