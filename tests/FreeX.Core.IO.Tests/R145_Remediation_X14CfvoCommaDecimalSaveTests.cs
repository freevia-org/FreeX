using System.Globalization;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Remediation for the r145 fix-wave gap: <c>NormalizeNumericCfvoValueForSave</c> was wired into
/// <c>ToCfvoXml</c> (the LEGACY worksheet-namespace <c>cfvo/@val</c> attribute writer) but not into
/// <c>ToX14DataBarCfvoXml</c> / <c>ToX14IconSetCfvoXml</c>, which write the same threshold value into
/// an <c>&lt;xm:f&gt;</c> CHILD ELEMENT inside the x14 extension block instead of a <c>val</c>
/// attribute. That block is the AUTHORITATIVE representation real Excel prefers when both the legacy
/// and x14 copies are present (see <c>RequiresGeneratedX14DataBar</c> /
/// <c>RequiresGeneratedOrExistingX14IconSet</c>'s doc comments), so a comma-decimal threshold still
/// reached the file Excel actually reads even after the r145 fix landed.
///
/// These tests cover exactly the two rule shapes the original R145 test file (ColorScale only, which
/// has no x14 twin) was silent on:
///  - a solid-fill DataBar (<see cref="ConditionalFormat.DataBarGradient"/> = false), which forces
///    <c>RequiresGeneratedX14DataBar</c> and is an ordinary Excel configuration, not an edge case.
///  - an x14-only icon-set gallery style ("3Stars"), which forces
///    <c>RequiresGeneratedOrExistingX14IconSet</c> via <c>IsX14OnlyIconSetStyle</c>.
///
/// Both go through the real <see cref="XlsxFileAdapter.Save"/> entry point under a temporarily-switched
/// de-DE <see cref="CultureInfo.CurrentCulture"/>, exactly like the original R145 tests, and assert the
/// x14 &lt;xm:f&gt; TEXT is invariant-dot.
/// </summary>
public sealed class R145_Remediation_X14CfvoCommaDecimalSaveTests
{
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace XmNs = "http://schemas.microsoft.com/office/excel/2006/main";

    // ── Fail-before proof: solid-fill DataBar with comma-decimal Number thresholds under de-DE ──

    [Fact]
    public void Save_SolidFillDataBarNumberThresholds_CommaDecimalUnderGermanCulture_WritesInvariantDotXmF()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var workbook = new Workbook("Test");
            var sheet = workbook.AddSheet("Sheet1");
            for (uint row = 1; row <= 3; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
                RuleType = CfRuleType.DataBar,
                Priority = 1,
                // Solid fill (not gradient) is an ordinary Excel configuration and, on its own,
                // already forces RequiresGeneratedX14DataBar -- no border/axis/color customization
                // needed to reach the x14-only write path.
                DataBarGradient = false,
                DataBarMinThresholdType = CfThresholdType.Number,
                DataBarMinThresholdValue = "12,5",
                DataBarMaxThresholdType = CfThresholdType.Number,
                DataBarMaxThresholdValue = "87,25",
            });

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(workbook, stream);

            var worksheet = XlsxPackageTestHelper.ReadWorksheetXml(stream);
            var x14DataBar = worksheet.Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
            var thresholds = x14DataBar.Elements(X14Ns + "cfvo").ToArray();
            thresholds.Should().HaveCount(2);

            // Must be invariant "12.5"/"87.25" in the <xm:f> CHILD ELEMENT -- NOT the raw locale text
            // "12,5"/"87,25". This is the representation real Excel prefers when both the legacy and
            // x14 blocks are present, so this is the copy that actually matters.
            thresholds[0].Element(XmNs + "f")?.Value.Should().Be("12.5");
            thresholds[1].Element(XmNs + "f")?.Value.Should().Be("87.25");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ── Fail-before proof: x14-only icon-set style with comma-decimal Percent thresholds under de-DE ──

    [Fact]
    public void Save_X14OnlyIconSetStyle_CommaDecimalPercentThresholds_UnderGermanCulture_WritesInvariantDotXmF()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var workbook = new Workbook("Test");
            var sheet = workbook.AddSheet("Sheet1");
            for (uint row = 1; row <= 3; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

            var cf = new ConditionalFormat
            {
                AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
                RuleType = CfRuleType.IconSet,
                Priority = 1,
                // "3Stars" has no member in the base ST_IconSetType enum, which forces
                // RequiresGeneratedOrExistingX14IconSet via IsX14OnlyIconSetStyle.
                IconSetStyle = "3Stars",
                IconSetShowValue = true,
                IconSetReverse = false,
            };
            cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percent, "0"));
            cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percent, "33,5"));
            cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percent, "67,25"));
            sheet.ConditionalFormats.Add(cf);

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(workbook, stream);

            var worksheet = XlsxPackageTestHelper.ReadWorksheetXml(stream);
            var x14IconSet = worksheet.Descendants(X14Ns + "iconSet").Should().ContainSingle().Subject;
            var thresholds = x14IconSet.Elements(X14Ns + "cfvo").ToArray();
            thresholds.Should().HaveCount(3);

            // Must be invariant "0"/"33.5"/"67.25" in the <xm:f> CHILD ELEMENT -- NOT the raw locale
            // text "33,5"/"67,25".
            thresholds[0].Element(XmNs + "f")?.Value.Should().Be("0");
            thresholds[1].Element(XmNs + "f")?.Value.Should().Be("33.5");
            thresholds[2].Element(XmNs + "f")?.Value.Should().Be("67.25");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ── Sibling/no-regression: values already stored in invariant "." form must round-trip unchanged ──

    [Fact]
    public void Save_SolidFillDataBarNumberThresholds_AlreadyInvariantDotValue_UnderGermanCulture_StaysUnchanged()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var workbook = new Workbook("Test");
            var sheet = workbook.AddSheet("Sheet1");
            for (uint row = 1; row <= 3; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
                RuleType = CfRuleType.DataBar,
                Priority = 1,
                DataBarGradient = false,
                DataBarMinThresholdType = CfThresholdType.Number,
                DataBarMinThresholdValue = "12.5",
                DataBarMaxThresholdType = CfThresholdType.Number,
                DataBarMaxThresholdValue = "100",
            });

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(workbook, stream);

            var worksheet = XlsxPackageTestHelper.ReadWorksheetXml(stream);
            var x14DataBar = worksheet.Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
            var thresholds = x14DataBar.Elements(X14Ns + "cfvo").ToArray();

            thresholds[0].Element(XmNs + "f")?.Value.Should().Be("12.5");
            thresholds[1].Element(XmNs + "f")?.Value.Should().Be("100");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
