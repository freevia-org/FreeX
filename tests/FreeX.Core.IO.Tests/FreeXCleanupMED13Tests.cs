using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Focused regression tests for FreeX cleanup batch MED13 (round-10 MED/LOW findings).
/// </summary>
public sealed class FreeXCleanupMED13Tests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace ChartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";

    /// <summary>
    /// P71: CopyChartExWithModeledContent must insert the generated legend BEFORE a source
    /// &lt;cx:extLst&gt;, never blindly append it after -- CT_Chart requires
    /// (title, plotArea, legend, extLst) order, and Excel repairs (discards) a chartEx part whose
    /// legend trails its extLst.
    /// </summary>
    [Fact]
    public void CopyChartExWithModeledContent_InsertsLegendBeforeTrailingExtLst()
    {
        var sourceXml = new XDocument(
            new XElement(ChartExNs + "chartSpace",
                new XElement(ChartExNs + "chartData"),
                new XElement(ChartExNs + "chart",
                    new XElement(ChartExNs + "title"),
                    new XElement(ChartExNs + "plotArea",
                        new XElement(ChartExNs + "plotAreaRegion")),
                    new XElement(ChartExNs + "extLst",
                        new XElement(ChartExNs + "ext", new XAttribute("uri", "{SOURCE-EXT}"))))));

        var generatedXml = new XDocument(
            new XElement(ChartExNs + "chartSpace",
                new XElement(ChartExNs + "chartData"),
                new XElement(ChartExNs + "chart",
                    new XElement(ChartExNs + "title"),
                    new XElement(ChartExNs + "plotArea",
                        new XElement(ChartExNs + "plotAreaRegion")),
                    new XElement(ChartExNs + "legend", new XAttribute("pos", "b")))));

        const string partPath = "xl/charts/chartEx1.xml";
        using var generatedArchive = new ZipArchive(new MemoryStream(), ZipArchiveMode.Update, leaveOpen: true);
        WriteEntry(generatedArchive, partPath, generatedXml);

        using var sourceArchive = new ZipArchive(new MemoryStream(), ZipArchiveMode.Update, leaveOpen: true);
        var sourceEntry = WriteEntry(sourceArchive, partPath, sourceXml);
        var generatedEntry = generatedArchive.GetEntry(partPath)!;

        InvokeCopyChartExWithModeledContent(sourceEntry, generatedEntry, generatedArchive);

        var resultXml = XlsxPackageXmlEditor.LoadXml(generatedArchive.GetEntry(partPath)!);
        var chartChildren = resultXml.Root!.Element(ChartExNs + "chart")!.Elements().Select(e => e.Name.LocalName).ToArray();

        chartChildren.Should().ContainInOrder("title", "plotArea", "legend", "extLst");
        chartChildren.Last().Should().Be("extLst", because: "extLst must remain the last CT_Chart child");
    }

    private static ZipArchiveEntry WriteEntry(ZipArchive archive, string path, XDocument xml)
    {
        var entry = archive.CreateEntry(path);
        using (var stream = entry.Open())
            xml.Save(stream);
        return archive.GetEntry(path)!;
    }

    private static void InvokeCopyChartExWithModeledContent(
        ZipArchiveEntry sourceEntry, ZipArchiveEntry generatedEntry, ZipArchive generatedArchive)
    {
        var method = typeof(XlsxFileAdapter).GetMethod(
            "CopyChartExWithModeledContent",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CopyChartExWithModeledContent method not found via reflection.");
        method.Invoke(null, [sourceEntry, generatedEntry, generatedArchive]);
    }

    /// <summary>
    /// P52: on save, classic (cellIs/expression) conditional-format rules must keep their true
    /// original file priority instead of being renumbered 1..N by ClosedXML's object model
    /// (which has no Priority property), which previously collided/inverted them against the
    /// advanced (dataBar/colorScale/iconSet/long-tail) rules' real priorities written verbatim.
    /// </summary>
    [Fact]
    public void SaveLoad_MixedClassicAndAdvancedRules_PreservesDistinctNonInvertedPriorityOrder()
    {
        // Real file priority order: dataBar(1), expression-with-StopIfTrue(2), iconSet(3), cellIs(4).
        using var source = XlsxPackageTestHelper.CreatePackageWithPatchedWorksheet(root =>
        {
            root.Add(
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "dataBar"),
                        new XAttribute("priority", "1"),
                        new XElement(MainNs + "dataBar",
                            new XElement(MainNs + "cfvo", new XAttribute("type", "min")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "max")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FF638EC6"))))),
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "B1:B5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "expression"),
                        new XAttribute("priority", "2"),
                        new XAttribute("stopIfTrue", "1"),
                        new XElement(MainNs + "formula", "B1>5"))),
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "C1:C5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "iconSet"),
                        new XAttribute("priority", "3"),
                        new XElement(MainNs + "iconSet",
                            new XElement(MainNs + "cfvo", new XAttribute("type", "percent"), new XAttribute("val", "0")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "percent"), new XAttribute("val", "33")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "percent"), new XAttribute("val", "67"))))),
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "D1:D5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "cellIs"),
                        new XAttribute("priority", "4"),
                        new XAttribute("operator", "greaterThan"),
                        new XElement(MainNs + "formula", "10"))));
        });

        var workbook = new XlsxFileAdapter().Load(source);
        using var resaved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, resaved);

        var cfRules = XlsxPackageTestHelper.ReadWorksheetXml(resaved)
            .Descendants(MainNs + "cfRule")
            .ToArray();

        cfRules.Should().HaveCount(4);
        var priorities = cfRules
            .Select(rule => int.Parse(rule.Attribute("priority")!.Value))
            .ToArray();

        // No two rules may collide on the same priority value.
        priorities.Distinct().Should().HaveCount(4, because: "classic and advanced rule families must share one collision-free priority sequence");

        // The dataBar (true priority 1) must still out-rank the expression rule (true priority 2):
        // a collision/renumbering bug previously let ClosedXML assign the classic rules priority
        // 1..N independent of the advanced rules, which could invert this relative order.
        var dataBarPriority = cfRules.Single(r => string.Equals(r.Attribute("type")?.Value, "dataBar", StringComparison.OrdinalIgnoreCase))
            .Attribute("priority")!.Value;
        var expressionPriority = cfRules.Single(r => string.Equals(r.Attribute("type")?.Value, "expression", StringComparison.OrdinalIgnoreCase))
            .Attribute("priority")!.Value;
        var iconSetPriority = cfRules.Single(r => string.Equals(r.Attribute("type")?.Value, "iconSet", StringComparison.OrdinalIgnoreCase))
            .Attribute("priority")!.Value;
        var cellIsPriority = cfRules.Single(r => string.Equals(r.Attribute("type")?.Value, "cellIs", StringComparison.OrdinalIgnoreCase))
            .Attribute("priority")!.Value;

        int.Parse(dataBarPriority).Should().BeLessThan(int.Parse(expressionPriority));
        int.Parse(expressionPriority).Should().BeLessThan(int.Parse(iconSetPriority));
        int.Parse(iconSetPriority).Should().BeLessThan(int.Parse(cellIsPriority));
    }
}
