using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Drawing;
using Free.Shared.Opc;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxFinalCoreIoTailDeduplicationTests
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    [Theory]
    [InlineData(null, BorderStyle.None)]
    [InlineData("thin", BorderStyle.Thin)]
    [InlineData("mediumDashDotDot", BorderStyle.MediumDashDotDot)]
    [InlineData(" Thin ", BorderStyle.None)]
    [InlineData("unknown", BorderStyle.None)]
    public void BorderStyleCodec_PreservesExactCaseSensitiveTokens(string? token, BorderStyle expected) =>
        XlsxBorderStyleCodec.Decode(token).Should().Be(expected);

    [Fact]
    public void WorksheetMetricSpanCalculator_PreservesDimensionsAndHiddenMetrics()
    {
        var workbook = new Workbook("metrics");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ColumnWidths[1] = 10;
        sheet.ColumnWidths[2] = 20;
        sheet.ColumnWidths[3] = 30;
        sheet.HiddenCols.Add(2);
        sheet.RowHeights[1] = 11;
        sheet.RowHeights[2] = 22;
        sheet.HiddenRows.Add(1);

        WorksheetMetricSpanCalculator.SumColumnPixels(sheet, 1, 3).Should().Be(320);
        WorksheetMetricSpanCalculator.SumRowPixels(sheet, 1, 2).Should().Be(22);
        WorksheetMetricSpanCalculator.SumColumnPixels(sheet, 1, 0).Should().Be(0);
    }

    [Fact]
    public void VmlVisibilityPolicy_ReplacesEveryVisibilityTokenWithoutReorderingOtherCss()
    {
        var shape = new XElement("shape", new XAttribute("style", "margin-left:1pt; VISIBILITY : hidden ;z-index:2;visibility:hidden"));

        XlsxVmlStylePolicy.SetVisibility(shape, isVisible: true).Should().BeTrue();
        shape.Attribute("style")!.Value.Should().Be("margin-left:1pt;visibility:visible;z-index:2;visibility:visible");
        XlsxVmlStylePolicy.SetVisibility(shape, isVisible: true).Should().BeFalse();
    }

    [Fact]
    public void XmlPolicies_PreserveRelationshipAndRevisionSemantics()
    {
        XNamespace revisionNs = "http://schemas.microsoft.com/office/spreadsheetml/2014/revision";
        var element = new XElement(
            SpreadsheetNs + "workbookView",
            new XAttribute(XNamespace.Xmlns + "xr", revisionNs),
            new XAttribute(revisionNs + "uid", "stale"),
            new XAttribute(RelationshipNs + "id", " rId7 "),
            new XAttribute("keep", "yes"));

        XlsxXmlNormalizationHelpers.NormalizeRelationshipId(element, RelationshipNs + "id").Should().BeTrue();
        XlsxXmlPreservationPolicy.RemoveOfficeRevisionAttributes(element);

        element.Attribute(RelationshipNs + "id")!.Value.Should().Be("rId7");
        element.Attribute("keep")!.Value.Should().Be("yes");
        element.Attributes().Should().NotContain(attribute => attribute.Name.Namespace == revisionNs);
        element.Attributes().Should().NotContain(attribute => attribute.IsNamespaceDeclaration && attribute.Value == revisionNs.NamespaceName);
    }

    [Fact]
    public void WorkbookExtensionListParentPolicy_KeepsFirstValidListAndForeignChildren()
    {
        XNamespace foreign = "urn:foreign";
        var first = new XElement(SpreadsheetNs + "extLst", new XElement(SpreadsheetNs + "ext", new XAttribute("uri", " urn:first ")));
        var second = new XElement(SpreadsheetNs + "extLst", new XElement(SpreadsheetNs + "ext", new XAttribute("uri", "urn:second")));
        var foreignList = new XElement(foreign + "extLst", new XElement(foreign + "ext"));
        var parent = new XElement(SpreadsheetNs + "workbookView", first, foreignList, second);

        XlsxWorkbookExtensionListNormalizer.NormalizeParent(parent).Should().BeTrue();

        parent.Elements().Should().Equal(first, foreignList);
        first.Element(SpreadsheetNs + "ext")!.Attribute("uri")!.Value.Should().Be("urn:first");
    }

    [Fact]
    public void ThemeSlotMapping_UsesOneWorkbookToDrawingMapping()
    {
        XlsxDrawingThemeColorSlots.ToSharedSlot(WorkbookThemeColorSlot.Dark1).Should().Be(DrawingMlThemeColorSlot.Dark1);
        XlsxDrawingThemeColorSlots.ToSharedSlot(WorkbookThemeColorSlot.Accent6).Should().Be(DrawingMlThemeColorSlot.Accent6);
        XlsxDrawingThemeColorSlots.ToSharedSlot(WorkbookThemeColorSlot.FollowedHyperlink).Should().Be(DrawingMlThemeColorSlot.FollowedHyperlink);
    }

    [Fact]
    public void SchemaWorksheetPipeline_VisitsEachWorksheetOnceAndRunsTheOrderedStepSet()
    {
        using var package = CreateWorkbookPackage();
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        var visited = new List<string>();

        XlsxWorksheetSinglePassNormalizer.NormalizeSchemaWorksheets(archive, visited.Add);

        visited.Should().Equal("xl/worksheets/sheet1.xml", "xl/worksheets/sheet2.xml");
        var first = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        first.Root!.Element(SpreadsheetNs + "dimension")!.Attribute("ref")!.Value.Should().Be("A1:A1");
        first.Root!.Elements().Select(element => element.Name.LocalName)
            .Should().Equal("dimension", "sheetViews", "sheetData");
    }

    [Fact]
    public void WorkbookWorksheetPathMap_PreservesWorkbookOrderAndResolvedTargets()
    {
        using var package = CreateWorkbookPackage();
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var map = XlsxWorkbookWorksheetPathMap.TryCreate(archive, rejectDuplicateRelationshipIds: true)!;

        map.Worksheets.Should().Equal(
            new XlsxWorkbookWorksheetPath("First", "xl/worksheets/sheet1.xml"),
            new XlsxWorkbookWorksheetPath("Second", "xl/worksheets/sheet2.xml"));
        map.SheetPathsByName["FIRST"].Should().Be("xl/worksheets/sheet1.xml");
    }

    [Fact]
    public void FinalTailCallers_AdoptSharedOwners()
    {
        TestWorkspaceFiles.ReadCoreIoSource("XlsxWorkbookSchemaNormalizer.cs")
            .Should().Contain("XlsxWorksheetSinglePassNormalizer.NormalizeSchemaWorksheets(archive)")
            .And.NotContain("XlsxWorksheetDimensionNormalizer.NormalizeWorksheets(archive)");

        foreach (var file in new[]
                 {
                     "XlsxStructuredTableWriter.cs",
                     "XlsxWorksheetChartWriter.cs",
                     "XlsxWorksheetDrawingObjectWriter.cs",
                     "XlsxStructuredTableMetadataReader.cs"
                 })
        {
            TestWorkspaceFiles.ReadCoreIoSource(file)
                .Should().Contain("XlsxWorkbookWorksheetPathMap.TryCreate")
                .And.NotContain("NormalizeWorkbookTarget(e.Attribute(\"Target\")!.Value)");
        }

        foreach (var file in new[] { "XlsxCellBorderStyleReader.cs", "XlsxDifferentialStyleReader.cs", "XlsxStructuredTableStyleMetadataReader.cs" })
            TestWorkspaceFiles.ReadCoreIoSource(file).Should().Contain("XlsxBorderStyleCodec.Decode");
    }

    private static MemoryStream CreateWorkbookPackage()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "xl/workbook.xml",
                """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="First" sheetId="1" r:id="rId1" />
                    <sheet name="Second" sheetId="2" r:id="rId2" />
                  </sheets>
                </workbook>
                """);
            WriteEntry(
                archive,
                "xl/_rels/workbook.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="worksheet" Target="worksheets/sheet1.xml" />
                  <Relationship Id="rId2" Type="worksheet" Target="worksheets/sheet2.xml" />
                </Relationships>
                """);
            WriteEntry(
                archive,
                "xl/worksheets/sheet1.xml",
                """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <dimension ref=" A1:A1 " />
                  <sheetViews><sheetView /></sheetViews>
                  <sheetData />
                </worksheet>
                """);
            WriteEntry(archive, "xl/worksheets/sheet2.xml", $"<worksheet xmlns=\"{SpreadsheetNs}\"><sheetData /></worksheet>");
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
