using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

// End-to-end regression for the Sample Dashboard fidelity fixes (2026-06-04). Each property below
// corresponds to a real round-trip bug found on a Google Sheets export and is covered in isolation by a
// focused unit test; this guards them together through the full source-package rebuild path (save -> load
// captures a source package -> edit forces a rebuild -> save -> reload), which is where several of the bugs
// only surfaced. Keeping them in one workbook also guards against cross-feature interactions.
public sealed class XlsxRoundTripFidelityRegressionTests
{
    [Fact]
    public void XlsxAdapter_SourcePackageRoundTrip_PreservesPartnerDashboardFidelityProperties()
    {
        var workbook = new Workbook("RoundTripFidelity");
        var data = workbook.AddSheet("Data");

        // (1) Quoted font name within Excel's 31-char limit must survive verbatim (Google export quirk).
        data.SetCell(new CellAddress(data.Id, 1, 1), new TextValue("Heading"));
        data.GetCell(1u, 1u)!.StyleId = workbook.RegisterStyle(
            new CellStyle { FontName = "\"Century Gothic\"" });

        // (2) Exact fractional column width must round-trip (no flooring / char-padding inflation).
        data.ColumnWidths[2] = 6.13;

        // (3) Hyperlink cell with an explicit black underlined font must not become theme-10 blue.
        var linkAddr = new CellAddress(data.Id, 1, 3);
        data.SetCell(linkAddr, new TextValue("link"));
        data.GetCell(1u, 3u)!.StyleId = workbook.RegisterStyle(
            new CellStyle { Underline = true, FontColor = new CellColor(0, 0, 0) });
        data.Hyperlinks[linkAddr] = "https://example.com/black";

        // (4) Two adjacent containsText CF rules with different differential-style fonts must not bleed into
        // each other through the dxf preservation merge (the green-fill rule must stay black, not turn red).
        data.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = GridRange.Parse("E1:E10", data.Id),
            Priority = 1,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "ok",
            FormulaText = "NOT(ISERROR(SEARCH((\"ok\"),(E1))))",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(0xD9, 0xEA, 0xD3), FillPatternStyle = CellFillPatternStyle.Solid },
        });
        data.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = GridRange.Parse("E1:E10", data.Id),
            Priority = 2,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "bad",
            FormulaText = "NOT(ISERROR(SEARCH((\"bad\"),(E1))))",
            FormatIfTrue = new CellStyle { FontColor = new CellColor(0xCC, 0, 0) },
        });

        // (5) A chart on a later sheet must stay on that sheet (not move onto the first sheet's drawing slot).
        var charts = workbook.AddSheet("Charts");
        charts.SetCell(new CellAddress(charts.Id, 1, 1), new TextValue("A"));
        charts.SetCell(new CellAddress(charts.Id, 1, 2), new NumberValue(10));
        charts.SetCell(new CellAddress(charts.Id, 2, 1), new TextValue("B"));
        charts.SetCell(new CellAddress(charts.Id, 2, 2), new NumberValue(20));
        charts.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(charts.Id, 1, 1), new CellAddress(charts.Id, 2, 2)),
        });

        var adapter = new XlsxFileAdapter();
        using var first = new MemoryStream();
        adapter.Save(workbook, first);
        first.Position = 0;
        var sourceLoaded = adapter.Load(first);
        // Force a rebuild (otherwise the fast byte-copy path round-trips trivially).
        sourceLoaded.GetSheetAt(0).SetCell(new CellAddress(sourceLoaded.GetSheetAt(0).Id, 500, 1), new NumberValue(1));
        using var second = new MemoryStream();
        adapter.Save(sourceLoaded, second);
        second.Position = 0;
        var reloaded = adapter.Load(second);

        var reloadedData = reloaded.GetSheetAt(0);

        // (1) font name
        reloaded.GetStyle(reloadedData.GetCell(1u, 1u)!.StyleId).FontName
            .Should().Be("\"Century Gothic\"", "a quoted font name within the 31-char limit must round-trip verbatim");

        // (2) column width
        reloadedData.ColumnWidths.TryGetValue(2u, out var width).Should().BeTrue();
        width.Should().BeApproximately(6.13, 1e-6);

        // (3) hyperlink font
        var linkStyle = reloaded.GetStyle(reloadedData.GetCell(1u, 3u)!.StyleId);
        linkStyle.FontColor.Should().Be(new CellColor(0, 0, 0), "an explicit black hyperlink font must not become theme-10 blue");
        linkStyle.Underline.Should().BeTrue();

        // (4) CF differential-style fonts did not bleed
        var okRule = reloadedData.ConditionalFormats.Single(cf => cf.TextRuleText == "ok");
        var badRule = reloadedData.ConditionalFormats.Single(cf => cf.TextRuleText == "bad");
        okRule.FormatIfTrue!.FontColor.Should().Be(new CellColor(0, 0, 0), "the green-fill rule must keep its black font, not inherit the red rule's font");
        okRule.FormatIfTrue!.FillColor.Should().Be(new CellColor(0xD9, 0xEA, 0xD3));
        badRule.FormatIfTrue!.FontColor.Should().Be(new CellColor(0xCC, 0, 0));

        // (5) chart placement
        reloadedData.Charts.Should().BeEmpty("the first sheet must not pick up the later sheet's chart");
        reloaded.GetSheetAt(1).Charts.Should().ContainSingle();
    }
}
