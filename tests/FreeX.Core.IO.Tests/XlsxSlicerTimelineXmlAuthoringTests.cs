using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxSlicerTimelineXmlAuthoringTests
{
    [Fact]
    public void BuildSlicerPart_EmitsExactNonDefaultControlXml()
    {
        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            Caption = "Region",
            StyleName = "SlicerStyleLight2",
            ColumnCount = 3,
            ShowCaption = false,
        };

        var xml = XlsxSlicerTimelineXmlAuthoring.BuildSlicerPart(slicer, "Slicer_Region");

        AssertExactXml(
            xml,
            """
            <slicers xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" mc:Ignorable="x" xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"><slicer name="Region Slicer" caption="Region" style="SlicerStyleLight2" cache="Slicer_Region" rowHeight="228600" columnCount="3" showCaption="0" /></slicers>
            """);
    }

    [Fact]
    public void BuildSlicerCacheDefinition_EmitsOrderedPivotBindingsAndSelectionExtension()
    {
        var workbook = new Workbook("Book");
        var cover = workbook.AddSheet("Cover");
        var data = workbook.AddSheet("Data");
        cover.PivotTables.Add(new PivotTableModel { Name = "CoverPivot" });
        data.PivotTables.Add(new PivotTableModel { Name = "DataPivot" });
        var workbookXml = XDocument.Parse(
            """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheets>
                <sheet name="Cover" sheetId="4" />
                <sheet name="Data" sheetId="27" />
              </sheets>
            </workbook>
            """);
        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            SourcePivotTableName = "DataPivot",
            SourceFieldName = "Region",
        };
        slicer.SelectedItems.AddRange(["North", "South"]);

        var xml = XlsxSlicerTimelineXmlAuthoring.BuildSlicerCacheDefinition(
            workbook,
            workbookXml,
            slicer,
            "Slicer_Region",
            ["DataPivot", "CoverPivot"]);

        AssertExactXml(
            xml,
            """
            <slicerCacheDefinition xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main" name="Slicer_Region" sourceName="Region" xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"><pivotTables><pivotTable name="DataPivot" tabId="27" /><pivotTable name="CoverPivot" tabId="4" /></pivotTables><extLst><x:ext uri="{9F2C6F77-9A06-4E1E-AF41-4DB3CB03A6A6}"><selectedItems xmlns="https://freex.local/xlsx/slicerTimelineState"><selectedItem value="North" /><selectedItem value="South" /></selectedItems></x:ext></extLst></slicerCacheDefinition>
            """);
    }

    [Fact]
    public void BuildSlicerCacheDefinition_KeepsTableBindingSeparateFromPivotXml()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        var slicer = new SlicerModel
        {
            Name = "Table Slicer",
            SourceFieldName = "Status",
            SourceTableId = 9,
            SourceTableColumnId = 11,
        };

        var xml = XlsxSlicerTimelineXmlAuthoring.BuildSlicerCacheDefinition(
            workbook,
            XDocument.Parse("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />"),
            slicer,
            "Slicer_Status",
            [null]);

        AssertExactXml(
            xml,
            """
            <slicerCacheDefinition xmlns:x15="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main" name="Slicer_Status" sourceName="Status" xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"><extLst><ext uri="{2F2917AC-EB37-4324-AD4E-5DD8C200BD13}" xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><x15:tableSlicerCache tableId="9" column="11" /></ext></extLst></slicerCacheDefinition>
            """);
        xml.Descendants().Should().NotContain(element => element.Name.LocalName == "pivotTables");
        xml.Descendants().Should().NotContain(element => element.Name.LocalName == "data");
    }

    [Fact]
    public void BuildTimelineXml_EmitsExactControlAndOrderedCacheBindings()
    {
        var timeline = new TimelineModel
        {
            Name = "Order Timeline",
            Caption = "Order date",
            StyleName = "TimeSlicerStyleLight1",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
            SelectedStartDate = "2024-02-01",
            SelectedEndDate = "2024-03-31",
            Level = 2,
            SelectionLevel = 3,
            ScrollPosition = "2024-01-01",
        };

        AssertExactXml(
            XlsxSlicerTimelineXmlAuthoring.BuildTimelinePart(timeline, "Timeline_OrderDate"),
            """
            <timelines xmlns="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main"><timeline name="Order Timeline" caption="Order date" style="TimeSlicerStyleLight1" cache="Timeline_OrderDate" level="2" selectionLevel="3" scrollPosition="2024-01-01T00:00:00" /></timelines>
            """);
        AssertExactXml(
            XlsxSlicerTimelineXmlAuthoring.BuildTimelineCacheDefinition(
                timeline,
                "Timeline_OrderDate",
                ["OrdersPivot", "ArchivePivot"]),
            """
            <timelineCacheDefinition name="Timeline_OrderDate" sourceName="OrderDate" startDate="2024-01-01" endDate="2024-12-31" selectedStartDate="2024-02-01" selectedEndDate="2024-03-31" xmlns="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main"><pivotTables><pivotTable name="OrdersPivot" /><pivotTable name="ArchivePivot" /></pivotTables></timelineCacheDefinition>
            """);
    }

    [Fact]
    public void PackageIndexAllocator_UsesSparseCaseInsensitivePartNamesWithoutCollisions()
    {
        using var package = new MemoryStream();
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        archive.CreateEntry("xl/slicerCaches/slicerCache1.xml");
        archive.CreateEntry("XL/SLICERCACHES/SLICERCACHE3.XML");
        archive.CreateEntry("xl/slicerCaches/not-a-cache.xml");
        archive.CreateEntry("xl/other/slicerCache2.xml");

        var used = XlsxSlicerTimelinePackageAuthoring.GetUsedPartIndices(
            archive,
            "xl/slicerCaches/",
            "slicerCache");

        used.Should().BeEquivalentTo([1, 3]);
        XlsxSlicerTimelinePackageAuthoring.AllocateNextPartIndex(used).Should().Be(2);
        XlsxSlicerTimelinePackageAuthoring.AllocateNextPartIndex(used).Should().Be(4);
        used.Should().BeEquivalentTo([1, 2, 3, 4]);
    }

    [Fact]
    public void WriterAndStateRewriter_DelegateSharedXmlAndIndexAuthoring()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var files = new[]
        {
            "XlsxSlicerTimelineWriter.cs",
            "XlsxSlicerTimelineStateRewriter.cs",
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "FreeX.Core.IO", file));
            source.Should().Contain("XlsxSlicerTimelineXmlAuthoring.BuildSlicerPart(", file);
            source.Should().Contain("XlsxSlicerTimelineXmlAuthoring.BuildSlicerCacheDefinition(", file);
            source.Should().Contain("XlsxSlicerTimelineXmlAuthoring.BuildTimelinePart(", file);
            source.Should().Contain("XlsxSlicerTimelineXmlAuthoring.BuildTimelineCacheDefinition(", file);
            source.Should().Contain("XlsxSlicerTimelinePackageAuthoring.GetUsedPartIndices(", file);
            source.Should().Contain("XlsxSlicerTimelinePackageAuthoring.AllocateNextPartIndex(", file);
            source.Should().NotContain("new XElement(SlicerXmlNs + \"slicers\"", file);
            source.Should().NotContain("new XElement(SlicerNs + \"slicers\"", file);
            source.Should().NotContain("new XElement(TimelineXmlNs + \"timelines\"", file);
            source.Should().NotContain("new XElement(TimelineNs + \"timelines\"", file);
            source.Should().NotContain("private static int AllocateNextIndex(", file);
        }
    }

    private static void AssertExactXml(XDocument actual, string expected)
    {
        var expectedXml = XDocument.Parse(expected, LoadOptions.PreserveWhitespace);
        actual.ToString(SaveOptions.DisableFormatting)
            .Should().Be(expectedXml.ToString(SaveOptions.DisableFormatting));
    }
}
