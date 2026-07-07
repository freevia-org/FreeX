using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for FreeX cleanup batch B2 (HIGH finding P50).
/// A classic (CellIs/Expression) conditional-format rule whose sqref lists multiple non-contiguous
/// ranges (e.g. "A1:A5 C1:C5") must preserve every range through XlsxConditionalFormatClosedXmlMapper.Load,
/// not just the first one. Before the fix, the mapper read only <c>xlCf.Range</c> (ClosedXML's first
/// range) and never consulted <c>IXLConditionalFormat.Ranges</c>, so AdditionalRanges stayed null and
/// the second (and any later) range silently stopped being highlighted -- then Save (which already
/// iterates cf.AllRanges correctly) would only ever see the one surviving range, permanently dropping
/// the rest of the rule's coverage on the very next save.
/// </summary>
public sealed class FreeXCleanupB2Tests
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Load_ClassicCellIsRuleWithMultiRangeSqref_PreservesAllRangesInAdditionalRanges()
    {
        using var source = XlsxPackageTestHelper.CreatePackageWithPatchedWorksheet(root =>
        {
            root.Add(new XElement(
                MainNs + "conditionalFormatting",
                new XAttribute("sqref", "A1:A5 C1:C5"),
                new XElement(
                    MainNs + "cfRule",
                    new XAttribute("type", "cellIs"),
                    new XAttribute("priority", "1"),
                    new XAttribute("operator", "greaterThan"),
                    new XElement(MainNs + "formula", "5"))));
        });

        var workbook = new XlsxFileAdapter().Load(source);
        var cf = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;

        cf.RuleType.Should().Be(CfRuleType.CellValue);
        cf.AppliesTo.Start.Col.Should().Be(1, "A1:A5 is the first sqref token");
        cf.AppliesTo.End.Col.Should().Be(1);

        cf.AdditionalRanges.Should().NotBeNull("the sqref had a second range (C1:C5) that must not be dropped");
        cf.AdditionalRanges!.Should().HaveCount(1);
        cf.AdditionalRanges![0].Start.Col.Should().Be(3, "C1:C5 is the second sqref token");
        cf.AdditionalRanges![0].End.Col.Should().Be(3);
        cf.AllRanges.Should().HaveCount(2);
    }

    [Fact]
    public void RoundTrip_ClassicExpressionRuleWithMultiRangeSqref_KeepsBothRangesAfterSaveAndReload()
    {
        using var source = XlsxPackageTestHelper.CreatePackageWithPatchedWorksheet(root =>
        {
            root.Add(new XElement(
                MainNs + "conditionalFormatting",
                new XAttribute("sqref", "B2:B4 D2:D4"),
                new XElement(
                    MainNs + "cfRule",
                    new XAttribute("type", "expression"),
                    new XAttribute("priority", "1"),
                    new XElement(MainNs + "formula", "B2>0"))));
        });

        var loaded = new XlsxFileAdapter().Load(source);
        var loadedCf = loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        loadedCf.AllRanges.Should().HaveCount(2, "both B2:B4 and D2:D4 must survive the initial load");

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, saved);
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);

        var reloadedCf = reloaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        reloadedCf.AllRanges.Should().HaveCount(2,
            "a rule that starts with two ranges must still have two ranges after a full load->save->reload cycle");
        var reloadedCols = reloadedCf.AllRanges.Select(r => r.Start.Col).OrderBy(c => c).ToList();
        reloadedCols.Should().Equal(2u, 4u);
    }
}
