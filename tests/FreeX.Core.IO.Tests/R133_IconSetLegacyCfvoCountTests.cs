using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 133 regression test, revised for the round-133-remediation fix:
/// <list type="bullet">
///   <item>
///     For an x14-only icon-set style whose real icon count is not 3 (currently only "5Boxes" --
///     the only x14-only style ("3Stars"/"3Triangles"/"5Boxes"/"NoIcons") with a non-3 icon count),
///     the legacy &lt;iconSet&gt; compatibility block written by
///     <see cref="XlsxAdvancedConditionalFormatWriter"/> must NOT be emitted with a &lt;cfvo&gt;
///     count that mismatches its (fixed 3-icon "3TrafficLights1") legacy fallback style -- OOXML's
///     CT_IconSet requires the child &lt;cfvo&gt; count to match the icon set's own icon count, and a
///     4/5-cfvo block under a 3-icon legacy type is schema-invalid (Excel repairs/strips it on
///     open). The original round-133 fix over-corrected this by omitting the legacy &lt;iconSet&gt;
///     block ENTIRELY in that case -- which means a reader that only understands the classic
///     &lt;cfRule&gt; block (no x14 extLst support) sees NO conditional-format rule at all for these
///     cells. That is worse than an approximate-but-schema-valid legacy rule, so the fix here must
///     instead downsample the real thresholds to a valid 3-icon approximation and keep writing the
///     legacy block, while the x14 extension block remains authoritative for readers that understand it.
///   </item>
/// </list>
/// </summary>
public sealed class R133_IconSetLegacyCfvoCountTests
{
    private const string WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private const string X14CfUri = "{78C0D931-6437-407d-A8EE-F0AAD7539E65}";

    [Fact]
    public void Save_X14OnlyFiveIconStyle_WritesValidThreeIconLegacyApproximationInsteadOfOmittingTheRule()
    {
        var workbook = new Workbook("CfX14OnlyFiveIconSet");
        var sheet = workbook.AddSheet("S1");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.IconSet,
            Priority = 1,
            IconSetStyle = "5Boxes",
            IconSetShowValue = true,
            IconSetReverse = false
        });

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        XNamespace worksheetNs = WorksheetNs;
        XNamespace x14Ns = X14Ns;

        // There is no valid 3-icon legacy analogue for a 5-icon x14-only style, so the legacy block
        // must be a downsampled 3-icon APPROXIMATION -- present and schema-valid -- rather than
        // either (a) a mismatched 5-cfvo block, or (b) omitted entirely. A legacy-only reader must
        // still see a usable icon-set rule.
        var legacyIconSet = worksheetXml.Descendants(worksheetNs + "iconSet")
            .Should().ContainSingle("a legacy-only reader (no x14 support) must still see a usable icon-set rule, " +
                "not nothing at all")
            .Subject;
        legacyIconSet.Attribute("iconSet")!.Value.Should().Be("3TrafficLights1",
            "the legacy fallback base style is always the fixed 3-icon '3TrafficLights1'");
        legacyIconSet.Elements(worksheetNs + "cfvo").Should().HaveCount(3,
            "the legacy <cfvo> count must match the legacy iconSet's own (3-icon) icon count");

        // The cfRule must still exist and still link to the x14 extension so the real style survives.
        var cfRule = worksheetXml.Descendants(worksheetNs + "cfRule")
            .Should().ContainSingle("the rule itself must still be written")
            .Subject;
        var extLst = cfRule.Element(worksheetNs + "extLst");
        extLst.Should().NotBeNull("the cfRule must link to the extended x14 rule via extLst/x14:id");
        var x14IdValue = extLst!.Descendants(x14Ns + "id").Should().ContainSingle().Subject.Value.Trim();

        var x14CfRule = worksheetXml.Root!
            .Elements(worksheetNs + "extLst")
            .Elements(worksheetNs + "ext")
            .Where(ext => ext.Attribute("uri")?.Value == X14CfUri)
            .Elements(x14Ns + "conditionalFormattings")
            .Elements(x14Ns + "conditionalFormatting")
            .Elements(x14Ns + "cfRule")
            .Should().ContainSingle("the x14 icon-set block must still be generated")
            .Subject;
        x14CfRule.Attribute("id")!.Value.Should().Be(x14IdValue);
        var x14IconSet = x14CfRule.Element(x14Ns + "iconSet");
        x14IconSet.Should().NotBeNull();
        x14IconSet!.Attribute("iconSet")!.Value.Should().Be("5Boxes",
            "the x14 block remains the AUTHORITATIVE representation of the real 5-icon style");
        x14IconSet.Elements(x14Ns + "cfvo").Should().HaveCount(5, "the x14 block carries the real 5-icon threshold set");

        // Round trip must still recover the real style (from the authoritative x14 block).
        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        var reloaded = loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        reloaded.RuleType.Should().Be(CfRuleType.IconSet);
        reloaded.IconSetStyle.Should().Be("5Boxes");

        // Validate the produced package with the real Open XML schema validator -- not just by eye.
        // No schema errors of ANY kind should be present (not just cfvo-count ones): the legacy
        // block must be a fully valid <iconSet>, not merely "less wrong".
        saved.Position = 0;
        using var document = SpreadsheetDocument.Open(saved, false);
        var validator = new OpenXmlValidator();
        var schemaErrors = validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .ToList();
        schemaErrors.Should().BeEmpty("the saved package -- including the downsampled legacy iconSet block -- must be schema-valid");
    }

    [Fact]
    public void Save_X14OnlyThreeIconStyle_StillWritesLegacyIconSetBlockNoRegression()
    {
        // Sibling/no-regression case: an x14-only style whose real icon count IS 3 (e.g. "3Stars")
        // has a valid 3-icon legacy analogue ("3TrafficLights1"), so the legacy block must still be
        // written with its own (unapproximated) real thresholds -- the downsampling path introduced
        // for the 5-icon case above must not kick in here.
        var workbook = new Workbook("CfX14OnlyThreeIconSet");
        var sheet = workbook.AddSheet("S1");
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            RuleType = CfRuleType.IconSet,
            Priority = 1,
            IconSetStyle = "3Stars"
        });

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        XNamespace worksheetNs = WorksheetNs;

        var legacyIconSet = worksheetXml.Descendants(worksheetNs + "iconSet")
            .Should().ContainSingle("a 3-icon x14-only style has a valid 3-icon legacy analogue and must keep the legacy block")
            .Subject;
        legacyIconSet.Attribute("iconSet")!.Value.Should().Be("3TrafficLights1");
        legacyIconSet.Elements(worksheetNs + "cfvo").Should().HaveCount(3);
    }
}
