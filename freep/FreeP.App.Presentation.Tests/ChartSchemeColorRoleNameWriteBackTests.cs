using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// theme-color-resolution F2: PptxChartWriter's BuildColorEl must preserve the authored OOXML
/// role name (e.g. "tx1"/"bg1"/"tx2"/"bg2") on write-back instead of collapsing it to the
/// slot's canonical name (e.g. "dk1"/"lt1"). A clrMapOvr on the slide/layout can remap
/// tx1/bg1/tx2/bg2 to a different dk*/lt* slot than the Office default, so baking the
/// currently-resolved slot into the chart XML silently changes the chart's color the next
/// time the deck is opened under a different effective clrMap.
/// </summary>
public sealed class ChartSchemeColorRoleNameWriteBackTests
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static ChartShape BuildChart(ChartType chartType)
    {
        var chart = new ChartShape { ChartType = chartType, RegenerateWorkbookOnSave = true };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3", "Q4" });
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange(new double?[] { 10, 12, 14, 15 });
        chart.Series.Add(series);
        return chart;
    }

    private static byte[] WriteDeck(ChartShape chart)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Chart",
            Kind = SlideShapeKind.Chart,
            ExtentCxEmu = 5_000_000,
            ExtentCyEmu = 3_000_000,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    private static XElement ChartSpaceSpPrSchemeClr(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = archive.Entries.Single(item =>
            item.FullName.StartsWith("ppt/charts/chart", StringComparison.OrdinalIgnoreCase) &&
            item.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        using var entryStream = entry.Open();
        var root = XDocument.Load(entryStream).Root!;
        // chartSpace/c:spPr/a:solidFill/a:schemeClr — the chart-area fill written by
        // BuildChartShapePropertiesEl(chart.ChartAreaFill, ...) via BuildColorEl.
        return root.Element(C + "spPr")!
            .Element(A + "solidFill")!
            .Element(A + "schemeClr")!;
    }

    [Fact]
    public void ChartAreaFill_AuthoredAsTx1Role_PreservesRoleNameOnWriteBack()
    {
        // Authored (read from XML) as schemeClr val="tx1": RoleName="tx1", Slot resolved via the
        // DEFAULT clrMap (tx1 -> Dk1). Under a clrMapOvr, "tx1" and "dk1" are NOT interchangeable
        // on write-back — only the raw role name round-trips correctly through remapping.
        var chart = BuildChart(ChartType.ColumnClustered);
        chart.ChartAreaFill = new ShapeFill.Solid(new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { RoleName = "tx1", Slot = ThemeColorSlot.Dk1, LumMod = 1.0 }));

        var bytes = WriteDeck(chart);
        var schemeClr = ChartSpaceSpPrSchemeClr(bytes);

        // Before the fix this writes "dk1" (PptxColorReader.ToSchemeColorString(Slot)), silently
        // discarding the tx1 indirection. After the fix it must write back "tx1" verbatim.
        schemeClr.Attribute("val")!.Value.Should().Be("tx1",
            "the writer must preserve the authored role name so a clrMapOvr's tx1->slot " +
            "indirection survives the round trip, not bake in the currently-resolved slot");
    }

    [Fact]
    public void ChartAreaFill_NoRoleNameCaptured_FallsBackToCanonicalSlotName()
    {
        // Sibling/no-regression case: a SchemeColorRef built programmatically (no RoleName, e.g.
        // tests or in-app color-picker construction) must still write a valid schemeClr using the
        // slot's canonical name -- this is the documented fallback in SchemeColorRef.RoleName's
        // XML doc comment ("Null/empty ... in that case Slot is used directly").
        var chart = BuildChart(ChartType.ColumnClustered);
        chart.ChartAreaFill = new ShapeFill.Solid(new ThemeAwareColor(
            SrgbColor.FromRgb(0x4472C4),
            new SchemeColorRef { RoleName = null, Slot = ThemeColorSlot.Accent1, LumMod = 1.0 }));

        var bytes = WriteDeck(chart);
        var schemeClr = ChartSpaceSpPrSchemeClr(bytes);

        schemeClr.Attribute("val")!.Value.Should().Be("accent1");
    }
}
