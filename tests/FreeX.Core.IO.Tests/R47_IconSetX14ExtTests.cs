using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 47 icon-set/x14-extension regression tests:
/// <list type="bullet">
///   <item>
///     R47-io-cf-icon-databar-ext-3-1 -- a per-icon override referencing an icon family that has no
///     member in the base ST_IconSetType enum (e.g. "NoIcons", chosen via FreeX's own "No Cell Icon"
///     Conditional Format dialog option) must not be written into the legacy &lt;cfIcon&gt; element
///     (schema-invalid: "NoIcons" is not a valid ST_IconSetType enumeration value); it must be routed
///     to the x14 extension instead, which the base schema's enum restriction does not apply to.
///   </item>
///   <item>
///     R47-io-cf-icon-databar-ext-3-2 -- "5Boxes" is a normal, gallery-selectable icon-set style
///     (<see cref="FreeX.App.Presentation.ConditionalFormatting.ConditionalFormatIconSetCatalog.GalleryOptions"/>)
///     that, like "3Stars"/"3Triangles", has no member in the base ST_IconSetType enum. Unlike those
///     two 3-icon styles, "5Boxes" has 5 icons, so writing its 5 real thresholds straight into the
///     3-icon legacy fallback type would be schema-mismatched OOXML. Rather than omit the legacy
///     compatibility block entirely (which leaves legacy-only readers with no rule at all), the
///     writer downsamples the real thresholds to a valid 3-icon approximation and still writes the
///     legacy block, while the x14 extension remains the authoritative full-fidelity representation
///     (round133-io-cf-iconset-legacy-cfvo-count; round133-remediation-io-cf-iconset-legacy-fallback).
///   </item>
///   <item>
///     R47-io-cf-icon-databar-ext-3-3 -- when FreeX itself generates an x14 icon-set block for a rule
///     that carries genuine per-icon overrides (Excel's "Custom" icon combination), the x14 CT_IconSet
///     "custom" attribute must be emitted, matching Excel's own behavior for a real Custom icon set.
///   </item>
/// </list>
/// </summary>
public sealed class R47_IconSetX14ExtTests
{
    private const string WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private const string X14CfUri = "{78C0D931-6437-407d-A8EE-F0AAD7539E65}";

    // ------------------------------------------------------------------------------------------
    // R47-io-cf-icon-databar-ext-3-1
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void Save_IconOverrideReferencingNoIcons_IsExcludedFromLegacyCfIconAndRoutedToX14()
    {
        var workbook = new Workbook("CfNoIconsOverride");
        var sheet = workbook.AddSheet("S1");
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var format = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            RuleType = CfRuleType.IconSet,
            Priority = 1,
            IconSetStyle = "3TrafficLights1",
            IconSetShowValue = true,
            IconSetReverse = false
        };
        // "No Cell Icon" in FreeX's Conditional Format dialog maps to exactly this override
        // (ConditionalFormatDialog.IconSets.cs's ChoiceToIconOverride).
        format.IconOverrides.Add(new CfIconOverride("NoIcons", 0));
        sheet.ConditionalFormats.Add(format);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        XNamespace worksheetNs = WorksheetNs;
        XNamespace x14Ns = X14Ns;

        // The legacy <iconSet> must carry NO cfIcon children at all: "NoIcons" is the only override
        // and it is not a legacy-valid ST_IconSetType member, so it must be entirely absent here.
        var legacyIconSet = worksheetXml.Descendants(worksheetNs + "iconSet").Should().ContainSingle().Subject;
        legacyIconSet.Attribute("iconSet")!.Value.Should().Be("3TrafficLights1");
        legacyIconSet.Elements(worksheetNs + "cfIcon").Should().BeEmpty(
            "an x14-only override family must never be written into the base-schema cfIcon element");

        // It must instead be carried through the x14 extension, which the writer must now generate
        // even though the overall style ("3TrafficLights1") is itself perfectly legacy-valid.
        var x14CfIcon = worksheetXml.Root!
            .Elements(worksheetNs + "extLst")
            .Elements(worksheetNs + "ext")
            .Where(ext => ext.Attribute("uri")?.Value == X14CfUri)
            .Elements(x14Ns + "conditionalFormattings")
            .Elements(x14Ns + "conditionalFormatting")
            .Elements(x14Ns + "cfRule")
            .Elements(x14Ns + "iconSet")
            .Elements(x14Ns + "cfIcon")
            .Should().ContainSingle("the NoIcons override must survive via the x14 extension, not be dropped")
            .Subject;
        x14CfIcon.Attribute("iconSet")!.Value.Should().Be("NoIcons");
        x14CfIcon.Attribute("iconId")!.Value.Should().Be("0");

        // No schema errors: this is the concrete schema-invalidity the finding reports fixed.
        saved.Position = 0;
        SchemaErrors(saved).Should().BeEmpty();

        // The override must survive a full round trip.
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedFormat = reloaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        reloadedFormat.IconSetStyle.Should().Be("3TrafficLights1");
        reloadedFormat.IconOverrides.Should().ContainSingle().Which.Should().Be(new CfIconOverride("NoIcons", 0));
    }

    [Fact]
    public void Save_IconOverrideReferencingLegacyFamily_StaysInLegacyCfIconNoRegression()
    {
        // Sibling/no-regression case: a per-icon override referencing an ORDINARY base-schema-valid
        // icon family must keep writing straight into the legacy <cfIcon> element exactly as before --
        // the new x14-only-override filter must not over-correct and start excluding legacy-valid
        // overrides too.
        var workbook = new Workbook("CfLegacyOverrideOnly");
        var sheet = workbook.AddSheet("S1");
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var format = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            RuleType = CfRuleType.IconSet,
            Priority = 1,
            IconSetStyle = "3TrafficLights1"
        };
        format.IconOverrides.Add(new CfIconOverride("3Arrows", 2));
        sheet.ConditionalFormats.Add(format);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        XNamespace worksheetNs = WorksheetNs;

        var legacyIconSet = worksheetXml.Descendants(worksheetNs + "iconSet").Should().ContainSingle().Subject;
        legacyIconSet.Element(worksheetNs + "cfIcon")!.Attribute("iconSet")!.Value.Should().Be("3Arrows");

        // No x14 machinery should be involved: the override family is legacy-valid, and the overall
        // style is not x14-only, so RequiresGeneratedOrExistingX14IconSet must stay false.
        worksheetXml.Descendants(worksheetNs + "ext")
            .Where(ext => ext.Attribute("uri")?.Value == X14CfUri)
            .Should().BeEmpty("a legacy-valid override must not gain an x14 link");

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle()
            .Which.IconOverrides.Should().ContainSingle().Which.Should().Be(new CfIconOverride("3Arrows", 2));
    }

    // ------------------------------------------------------------------------------------------
    // R47-io-cf-icon-databar-ext-3-2
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void Save_FiveBoxesGalleryStyle_WritesApproximateLegacyBlockAndEmitsX14Block()
    {
        var workbook = new Workbook("CfFiveBoxes");
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

        // "5Boxes" has no member in the base ST_IconSetType enum, and unlike "3Stars"/"3Triangles" it
        // has 5 icons rather than 3, so there is no EXACT 3-icon legacy analogue -- but a downsampled
        // 3-icon APPROXIMATION must still be written so legacy-only readers get a usable rule instead
        // of nothing (round133-remediation-io-cf-iconset-legacy-fallback).
        var legacyIconSet = worksheetXml.Descendants(worksheetNs + "iconSet")
            .Should().ContainSingle("a legacy-only reader must still see a usable icon-set rule, not nothing")
            .Subject;
        legacyIconSet.Attribute("iconSet")!.Value.Should().Be("3TrafficLights1");
        legacyIconSet.Elements(worksheetNs + "cfvo").Should().HaveCount(3,
            "the legacy <cfvo> count must match the legacy iconSet's own (3-icon) icon count");

        var x14IconSet = worksheetXml.Root!
            .Elements(worksheetNs + "extLst")
            .Elements(worksheetNs + "ext")
            .Where(ext => ext.Attribute("uri")?.Value == X14CfUri)
            .Elements(x14Ns + "conditionalFormattings")
            .Elements(x14Ns + "conditionalFormatting")
            .Elements(x14Ns + "cfRule")
            .Elements(x14Ns + "iconSet")
            .Should().ContainSingle("the real x14-only style must be carried in the extension, not omitted")
            .Subject;
        x14IconSet.Attribute("iconSet")!.Value.Should().Be("5Boxes");
        x14IconSet.Elements(x14Ns + "cfvo").Should().HaveCount(5, "the x14 block carries the full 5-icon threshold list");

        saved.Position = 0;
        SchemaErrors(saved).Should().BeEmpty();

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle()
            .Which.IconSetStyle.Should().Be("5Boxes", "the real style must survive the round trip");
    }

    [Fact]
    public void Save_OrdinaryGalleryStyle_StaysLegacyOnlyNoRegression()
    {
        // Sibling/no-regression case: an ordinary (non-x14-only) gallery style must be completely
        // unaffected by adding "5Boxes" to the x14-only set.
        var workbook = new Workbook("CfOrdinaryGalleryStyle");
        var sheet = workbook.AddSheet("S1");
        for (uint row = 1; row <= 4; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            RuleType = CfRuleType.IconSet,
            Priority = 1,
            IconSetStyle = "4Rating",
        });

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        XNamespace worksheetNs = WorksheetNs;

        var legacyIconSet = worksheetXml.Descendants(worksheetNs + "iconSet").Should().ContainSingle().Subject;
        legacyIconSet.Attribute("iconSet")!.Value.Should().Be("4Rating");
        worksheetXml.Descendants(worksheetNs + "ext")
            .Where(ext => ext.Attribute("uri")?.Value == X14CfUri)
            .Should().BeEmpty("an ordinary gallery style must not gain an x14 link");

        saved.Position = 0;
        SchemaErrors(saved).Should().BeEmpty();
    }

    // ------------------------------------------------------------------------------------------
    // R47-io-cf-icon-databar-ext-3-3
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void Save_GeneratedX14IconSetWithOverrides_EmitsCustomAttribute()
    {
        var workbook = new Workbook("CfCustomIconSet");
        var sheet = workbook.AddSheet("S1");
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var format = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            RuleType = CfRuleType.IconSet,
            Priority = 1,
            IconSetStyle = "3Stars", // x14-only, forces x14 block generation regardless of overrides
            IconSetShowValue = true,
            IconSetReverse = false
        };
        format.IconOverrides.Add(new CfIconOverride("NoIcons", 1));
        sheet.ConditionalFormats.Add(format);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        XNamespace worksheetNs = WorksheetNs;
        XNamespace x14Ns = X14Ns;

        var x14IconSet = worksheetXml.Root!
            .Elements(worksheetNs + "extLst")
            .Elements(worksheetNs + "ext")
            .Where(ext => ext.Attribute("uri")?.Value == X14CfUri)
            .Elements(x14Ns + "conditionalFormattings")
            .Elements(x14Ns + "conditionalFormatting")
            .Elements(x14Ns + "cfRule")
            .Elements(x14Ns + "iconSet")
            .Should().ContainSingle()
            .Subject;

        x14IconSet.Attribute("custom")!.Value.Should().Be("1",
            "a rule with genuine per-icon overrides is Excel's Custom icon combination");

        saved.Position = 0;
        SchemaErrors(saved).Should().BeEmpty();
    }

    [Fact]
    public void Save_GeneratedX14IconSetWithoutOverrides_OmitsCustomAttributeNoRegression()
    {
        // Sibling/no-regression case: a plain x14-only style with NO per-icon overrides is not a
        // "Custom" icon combination -- the custom attribute must stay omitted, exactly as before this
        // fix (matching R25's fresh-generated-block assertions, which never expected a custom attribute).
        var workbook = new Workbook("CfPlainX14StyleNoOverrides");
        var sheet = workbook.AddSheet("S1");
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            RuleType = CfRuleType.IconSet,
            Priority = 1,
            IconSetStyle = "3Stars",
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

        var x14IconSet = worksheetXml.Root!
            .Elements(worksheetNs + "extLst")
            .Elements(worksheetNs + "ext")
            .Where(ext => ext.Attribute("uri")?.Value == X14CfUri)
            .Elements(x14Ns + "conditionalFormattings")
            .Elements(x14Ns + "conditionalFormatting")
            .Elements(x14Ns + "cfRule")
            .Elements(x14Ns + "iconSet")
            .Should().ContainSingle()
            .Subject;

        x14IconSet.Attribute("custom").Should().BeNull("no per-icon overrides means this is not a Custom icon set");

        saved.Position = 0;
        SchemaErrors(saved).Should().BeEmpty();
    }

    private static System.Collections.Generic.List<string> SchemaErrors(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        if (stream.CanSeek)
            stream.Position = originalPosition;
        copy.Position = 0;
        using var document = SpreadsheetDocument.Open(copy, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }
}
