using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void SlicerTimelinePackage_ProducesSchemaValidWorkbook()
    {
        using var stream = Save(CreateSlicerTimelineSourceWorkbook());

        SchemaErrors(stream).Should().BeEmpty();
        AssertSlicerTimelinePackageGraph(stream);
    }

    [Fact]
    public void LoadedWorkbookFullSave_WithSlicerTimelinePackage_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateSlicerTimelineSourceWorkbook());

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        SchemaErrors(saved).Should().BeEmpty();
        saved.Position = 0;
        XlsxPackageHealthValidator.Validate(saved).Should().BeEmpty();
        AssertSlicerTimelinePackageGraph(saved);

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.Slicers.Should().ContainSingle()
            .Which.SelectedItems.Should().Equal("East", "West");
        reloaded.Timelines.Should().ContainSingle()
            .Which.SelectedStartDate.Should().Be("2026-03-01");
    }

    private static Workbook CreateSlicerTimelineSourceWorkbook()
    {
        var workbook = new Workbook("SlicerTimelineSchema");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            StyleName = "SlicerStyleLight2"
        };
        slicer.SelectedItems.AddRange(["East", "West"]);
        workbook.Slicers.Add(slicer);
        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            Caption = "Order Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
            StyleName = "TimeSlicerStyleLight1",
            StartDate = "2026-01-01",
            EndDate = "2026-06-30",
            SelectedStartDate = "2026-03-01",
            SelectedEndDate = "2026-04-30"
        });

        return workbook;
    }

    private static void AssertSlicerTimelinePackageGraph(Stream stream)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypes = ReadPackageRootElement(stream, "[Content_Types].xml")
            .Elements(contentTypeNs + "Override")
            .ToList();
        contentTypes.Should().Contain(element =>
            SlicerTimelineAttributeValue(element, "PartName") == "/xl/slicers/slicer1.xml" &&
            SlicerTimelineAttributeValue(element, "ContentType") == "application/vnd.ms-excel.slicer+xml");
        contentTypes.Should().Contain(element =>
            SlicerTimelineAttributeValue(element, "PartName") == "/xl/slicerCaches/slicerCache1.xml" &&
            SlicerTimelineAttributeValue(element, "ContentType") == "application/vnd.ms-excel.slicerCache+xml");
        contentTypes.Should().Contain(element =>
            SlicerTimelineAttributeValue(element, "PartName") == "/xl/timelines/timeline1.xml" &&
            SlicerTimelineAttributeValue(element, "ContentType") == "application/vnd.ms-excel.Timeline+xml");
        contentTypes.Should().Contain(element =>
            SlicerTimelineAttributeValue(element, "PartName") == "/xl/timelineCaches/timelineCache1.xml" &&
            SlicerTimelineAttributeValue(element, "ContentType") == "application/vnd.ms-excel.TimelineCache+xml");

        ReadPackageRootElement(stream, "xl/_rels/workbook.xml.rels")
            .Elements(packageRelationshipNs + "Relationship")
            .Should()
            .Contain(element =>
                SlicerTimelineAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2007/relationships/slicerCache" &&
                SlicerTimelineAttributeValue(element, "Target") == "slicerCaches/slicerCache1.xml")
            .And
            .Contain(element =>
                SlicerTimelineAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2010/relationships/TimelineCache" &&
                SlicerTimelineAttributeValue(element, "Target") == "timelineCaches/timelineCache1.xml");

        ReadPackageRootElement(stream, "xl/worksheets/_rels/sheet1.xml.rels")
            .Elements(packageRelationshipNs + "Relationship")
            .Should()
            .Contain(element =>
                SlicerTimelineAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2007/relationships/slicer" &&
                SlicerTimelineAttributeValue(element, "Target") == "../slicers/slicer1.xml")
            .And
            .Contain(element =>
                SlicerTimelineAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2010/relationships/Timeline" &&
                SlicerTimelineAttributeValue(element, "Target") == "../timelines/timeline1.xml");

        ReadPackageRootElement(stream, "xl/slicers/_rels/slicer1.xml.rels")
            .Elements(packageRelationshipNs + "Relationship")
            .Should()
            .ContainSingle(element =>
                SlicerTimelineAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2007/relationships/slicerCache" &&
                SlicerTimelineAttributeValue(element, "Target") == "../slicerCaches/slicerCache1.xml");
        ReadPackageRootElement(stream, "xl/timelines/_rels/timeline1.xml.rels")
            .Elements(packageRelationshipNs + "Relationship")
            .Should()
            .ContainSingle(element =>
                SlicerTimelineAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2010/relationships/TimelineCache" &&
                SlicerTimelineAttributeValue(element, "Target") == "../timelineCaches/timelineCache1.xml");

        ReadWorkbookChildElement(stream, "extLst")
            .Descendants()
            .Select(element => element.Name.LocalName)
            .Should()
            .Contain(["slicerCaches", "slicerCache", "timelineCacheRefs", "timelineCacheRef"]);

        var worksheetRoot = ReadPackageRootElement(stream, "xl/worksheets/sheet1.xml");
        worksheetRoot.Elements().Last().Name.LocalName.Should().Be("extLst");
        worksheetRoot.Descendants()
            .Select(element => element.Name.LocalName)
            .Should()
            .Contain(["slicerList", "slicer", "timelineRefs", "timelineRef"]);

        ReadPackageRootElement(stream, "xl/slicers/slicer1.xml")
            .Descendants()
            .Should()
            .Contain(element => element.Name.LocalName == "slicer");
        ReadPackageRootElement(stream, "xl/timelines/timeline1.xml")
            .Descendants()
            .Should()
            .Contain(element => element.Name.LocalName == "timeline");
    }

    private static string? SlicerTimelineAttributeValue(XElement element, string name) =>
        element.Attribute(name)?.Value;
}
