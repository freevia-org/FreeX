using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round 145's culture-io F1 finding:
/// <see cref="XlsxAdvancedConditionalFormatWriter"/>'s ToCfvoXml wrote a Color-Scale/Data-Bar/Icon-Set
/// Number/Percent/Percentile threshold's <c>val</c> attribute verbatim from the in-memory model string
/// -- which itself is captured verbatim from a plain, unmasked text box (ConditionalFormatDialog.Result.cs
/// / ConditionalFormatRuleBuilder.cs). On a comma-decimal culture (e.g. de-DE) a fractional value like
/// "12,5" therefore landed in the saved .xlsx as the schema-invalid literal <c>&lt;cfvo type="num"
/// val="12,5"/&gt;</c> instead of the locale-invariant "12.5" OOXML requires.
///
/// The fix normalizes the value at the single write choke point (ToCfvoXml -> NormalizeNumericCfvoValueForSave)
/// by re-parsing it under the current UI culture first (so "12,5" becomes 12.5) and falling back to
/// invariant-culture parsing, then always re-emitting via <see cref="double.ToString(IFormatProvider?)"/>
/// under <see cref="CultureInfo.InvariantCulture"/>.
///
/// These tests go through the real <see cref="XlsxFileAdapter.Save"/> entry point and temporarily switch
/// the executing thread's <see cref="CultureInfo.CurrentCulture"/> to de-DE to reproduce the locale
/// dependency, always restoring it in a finally block.
/// </summary>
public sealed class R145_CfvoCommaDecimalCultureSaveTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── Fail-before proof: a comma-decimal fractional Color-Scale threshold under de-DE ──

    [Fact]
    public void Save_ColorScaleNumberThreshold_CommaDecimalUnderGermanCulture_WritesInvariantDotValue()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            // Exactly the in-memory shape ConditionalFormatRuleBuilder produces when a user on a
            // comma-decimal locale types "12,5" into the Color Scale Minimum "Number" value box.
            var workbook = new Workbook("Test");
            var sheet = workbook.AddSheet("Sheet1");
            for (uint row = 1; row <= 3; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
                RuleType = CfRuleType.ColorScale,
                Priority = 1,
                UseThreeColorScale = false,
                MinThresholdType = CfThresholdType.Number,
                MinThresholdValue = "12,5",
                MaxThresholdType = CfThresholdType.Number,
                MaxThresholdValue = "87,25",
                MinColor = new RgbColor(255, 0, 0),
                MaxColor = new RgbColor(0, 255, 0),
            });

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(workbook, stream);

            var cfvoValues = ReadColorScaleCfvoValues(stream);

            // Must be invariant "12.5"/"87.25" -- NOT the raw locale text "12,5"/"87,25", which is
            // schema-invalid OOXML and, per ConditionalFormatEvaluationMath.TryParseInvariant's use of
            // NumberStyles.Any, would silently misparse back to 125/8725 on FreeX's own re-read.
            cfvoValues.Should().Equal("12.5", "87.25");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ── Sibling/no-regression: a value already stored in invariant "." form must round-trip unchanged ──

    [Fact]
    public void Save_ColorScaleNumberThreshold_AlreadyInvariantDotValue_UnderGermanCulture_StaysUnchanged()
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
                RuleType = CfRuleType.ColorScale,
                Priority = 1,
                UseThreeColorScale = false,
                MinThresholdType = CfThresholdType.Number,
                MinThresholdValue = "12.5",
                MaxThresholdType = CfThresholdType.Number,
                MaxThresholdValue = "100",
                MinColor = new RgbColor(255, 0, 0),
                MaxColor = new RgbColor(0, 255, 0),
            });

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(workbook, stream);

            var cfvoValues = ReadColorScaleCfvoValues(stream);

            cfvoValues.Should().Equal("12.5", "100");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static List<string> ReadColorScaleCfvoValues(MemoryStream savedStream)
    {
        savedStream.Position = 0;
        using var archive = new ZipArchive(savedStream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new InvalidOperationException("xl/worksheets/sheet1.xml not found in saved package.");
        using var entryStream = worksheetEntry.Open();
        var worksheetXml = XDocument.Load(entryStream);

        return worksheetXml.Descendants(WorksheetNs + "colorScale")
            .Elements(WorksheetNs + "cfvo")
            .Select(cfvo => cfvo.Attribute("val")!.Value)
            .ToList();
    }
}
