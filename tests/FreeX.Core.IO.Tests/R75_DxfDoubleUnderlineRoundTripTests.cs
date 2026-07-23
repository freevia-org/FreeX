using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R75-io-styles-fonts-4-2: a conditional-format differential style
/// (dxf) font underline never distinguished double from single.
/// <see cref="XlsxDifferentialStyleReader"/>.Read set <c>style.Underline</c> from the mere
/// presence of a <c>&lt;u/&gt;</c> element and never inspected its <c>val</c> attribute, so a
/// dxf carrying <c>&lt;u val="double"/&gt;</c> read back as plain single underline. The paired
/// writer (<see cref="XlsxAdvancedConditionalFormatWriter"/>.ToDifferentialStyleXml) always
/// emitted a bare <c>&lt;u/&gt;</c>, so a freshly-authored double-underline CF rule saved as
/// single underline too. Both are fixed to mirror how the primary cell-style path
/// (<see cref="XlsxClosedXmlCellMapper"/>.MapStyle/ApplyStyle) already treats double vs single
/// underline as distinct states.
///
/// These findings only affect "advanced" conditional-format rule types (colorScale/dataBar/
/// iconSet/aboveAverage/top10/uniqueValues/duplicateValues/text-rules/dateOccurring/blanks/
/// errors) — see <see cref="XlsxAdvancedConditionalFormatMetadata.IsAdvancedConditionalFormat"/>.
/// Classic cellIs/expression rules go through ClosedXML's own conditional-format object model
/// (<c>XlsxConditionalFormatClosedXmlMapper</c>), a separate code path already covered by
/// R22_CfStyleRoundTripTests and unaffected by this change.
/// </summary>
public sealed class R75_DxfDoubleUnderlineRoundTripTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ---- Direct reader coverage (XlsxDifferentialStyleReader.Read) ----

    [Theory]
    [InlineData("double")]
    [InlineData("doubleAccounting")]
    public void DifferentialStyleReader_ReadsDoubleUnderlineVal_AsDistinctFromSingle(string underlineVal)
    {
        var dxf = XElement.Parse(
            $"""<dxf xmlns="{WorkbookNs}"><font><u val="{underlineVal}"/></font></dxf>""");

        var style = XlsxDifferentialStyleReader.ReadDifferentialStyle(dxf, WorkbookNs);

        style.Underline.Should().BeTrue($"a dxf <u val=\"{underlineVal}\"/> is still an underline");
        style.DoubleUnderline.Should().BeTrue(
            $"a dxf <u val=\"{underlineVal}\"/> must be modeled as double underline, not downgraded to single");
    }

    [Fact]
    public void DifferentialStyleReader_ReadsBareUnderlineElement_AsSingle_NotDouble()
    {
        // Sibling/no-regression: a bare <u/> (no val attribute, OOXML's default meaning "single")
        // must stay single, not be swept up by the double-underline fix.
        var dxf = XElement.Parse($"""<dxf xmlns="{WorkbookNs}"><font><u/></font></dxf>""");

        var style = XlsxDifferentialStyleReader.ReadDifferentialStyle(dxf, WorkbookNs);

        style.Underline.Should().BeTrue();
        style.DoubleUnderline.Should().BeFalse("a bare <u/> element means single underline in OOXML");
    }

    [Fact]
    public void DifferentialStyleReader_NoUnderlineElement_ReadsNeitherUnderlineFlag()
    {
        // Sibling/no-regression: a dxf font with no <u> at all must not set either flag.
        var dxf = XElement.Parse($"""<dxf xmlns="{WorkbookNs}"><font><b/></font></dxf>""");

        var style = XlsxDifferentialStyleReader.ReadDifferentialStyle(dxf, WorkbookNs);

        style.Underline.Should().BeFalse();
        style.DoubleUnderline.Should().BeFalse();
        style.Bold.Should().BeTrue("the sibling <b/> element must still be read normally");
    }

    // ---- End-to-end coverage through the real advanced-CF writer + reader ----

    [Fact]
    public void XlsxAdapter_AdvancedCfRuleWithDoubleUnderline_RoundTrips_AsDoubleUnderline()
    {
        var workbook = new Workbook("DxfDoubleUnderlineRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("urgent memo"));

        // ContainsText is one of the "advanced" rule types, so its FormatIfTrue is written/read
        // through XlsxAdvancedConditionalFormatWriter.DifferentialStyles.cs / XlsxDifferentialStyleReader
        // rather than ClosedXML's own conditional-format object model.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "urgent",
            FormatIfTrue = new CellStyle { DoubleUnderline = true },
        });

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var rule = reloaded.GetSheetAt(0)!.ConditionalFormats.Should().ContainSingle().Subject;
        rule.FormatIfTrue.Should().NotBeNull();
        rule.FormatIfTrue!.Underline.Should().BeTrue();
        rule.FormatIfTrue!.DoubleUnderline.Should().BeTrue(
            "an advanced CF rule's double-underline dxf must survive save/reload instead of downgrading to single");
    }

    [Fact]
    public void XlsxAdapter_AdvancedCfRuleWithSingleUnderline_RoundTrips_AsSingle_NoRegression()
    {
        var workbook = new Workbook("DxfSingleUnderlineRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("urgent memo"));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "urgent",
            FormatIfTrue = new CellStyle { Underline = true },
        });

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var rule = reloaded.GetSheetAt(0)!.ConditionalFormats.Should().ContainSingle().Subject;
        rule.FormatIfTrue.Should().NotBeNull();
        rule.FormatIfTrue!.Underline.Should().BeTrue();
        rule.FormatIfTrue!.DoubleUnderline.Should().BeFalse(
            "a plain single-underline advanced CF rule must not be upgraded to double by the fix");
    }

    [Fact]
    public void XlsxAdapter_AdvancedCfRuleWithNoUnderline_RoundTrips_WithNeitherFlagSet()
    {
        var workbook = new Workbook("DxfNoUnderlineRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("urgent memo"));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "urgent",
            FormatIfTrue = new CellStyle { Bold = true },
        });

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var rule = reloaded.GetSheetAt(0)!.ConditionalFormats.Should().ContainSingle().Subject;
        rule.FormatIfTrue.Should().NotBeNull();
        rule.FormatIfTrue!.Bold.Should().BeTrue("the sibling Bold property must still round-trip normally");
        rule.FormatIfTrue!.Underline.Should().BeFalse();
        rule.FormatIfTrue!.DoubleUnderline.Should().BeFalse();
    }
}
