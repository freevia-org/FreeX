using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxSlicerTimelinePackageRelationshipTests
{
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace SlicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace TimelineNs = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

    [Fact]
    public void MergeRelationshipParts_PreservesSlicerTimelineGraphToGeneratedPartsAndRebindsCollidingRefs()
    {
        using var sourcePackage = CreateSlicerTimelineSourcePackage();
        using var targetPackage = CreateGeneratedSlicerTimelineTargetPackage();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        generatedEntriesBeforeMerge.Should().Contain([
            "xl/workbook.xml",
            "xl/worksheets/sheet1.xml",
            "xl/slicers/slicer1.xml",
            "xl/slicerCaches/slicerCache1.xml",
            "xl/timelines/timeline1.xml",
            "xl/timelineCaches/timelineCache1.xml"
        ]);

        XlsxPackageMetadataMerger.MergeContentTypes(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);

        AssertSlicerTimelineContentTypes(targetArchive);

        var workbookRels = LoadRelationships(targetArchive, "xl/_rels/workbook.xml.rels");
        var slicerCacheRelId = AssertRelationshipRebound(
            workbookRels,
            "rIdSlicerCache",
            "http://schemas.microsoft.com/office/2007/relationships/slicerCache",
            "slicerCaches/slicerCache1.xml");
        var timelineCacheRelId = AssertRelationshipRebound(
            workbookRels,
            "rIdTimelineCache",
            "http://schemas.microsoft.com/office/2010/relationships/TimelineCache",
            "timelineCaches/timelineCache1.xml");

        var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(targetArchive, "xl/workbook.xml");
        workbookXml.Root!
            .Descendants(SlicerNs + "slicerCache")
            .Should()
            .ContainSingle(element => (string?)element.Attribute(RelNs + "id") == slicerCacheRelId);
        workbookXml.Root!
            .Descendants(TimelineNs + "timelineCacheRef")
            .Should()
            .ContainSingle(element => (string?)element.Attribute(RelNs + "id") == timelineCacheRelId);

        var sheetRels = LoadRelationships(targetArchive, "xl/worksheets/_rels/sheet1.xml.rels");
        var slicerRelId = AssertRelationshipRebound(
            sheetRels,
            "rIdSlicer",
            "http://schemas.microsoft.com/office/2007/relationships/slicer",
            "../slicers/slicer1.xml");
        var timelineRelId = AssertRelationshipRebound(
            sheetRels,
            "rIdTimeline",
            "http://schemas.microsoft.com/office/2010/relationships/Timeline",
            "../timelines/timeline1.xml");

        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(targetArchive, "xl/worksheets/sheet1.xml");
        worksheetXml.Root!
            .Descendants(SlicerNs + "slicer")
            .Should()
            .ContainSingle(element => (string?)element.Attribute(RelNs + "id") == slicerRelId);
        worksheetXml.Root!
            .Descendants(TimelineNs + "timelineRef")
            .Should()
            .ContainSingle(element => (string?)element.Attribute(RelNs + "id") == timelineRelId);

        var slicerPartRels = LoadRelationships(targetArchive, "xl/slicers/_rels/slicer1.xml.rels");
        slicerPartRels.Should().ContainSingle(element =>
            (string?)element.Attribute("Type") == "http://schemas.microsoft.com/office/2007/relationships/slicerCache" &&
            (string?)element.Attribute("Target") == "../slicerCaches/slicerCache1.xml");

        var timelinePartRels = LoadRelationships(targetArchive, "xl/timelines/_rels/timeline1.xml.rels");
        timelinePartRels.Should().ContainSingle(element =>
            (string?)element.Attribute("Type") == "http://schemas.microsoft.com/office/2010/relationships/TimelineCache" &&
            (string?)element.Attribute("Target") == "../timelineCaches/timelineCache1.xml");
    }

    private static List<XElement> LoadRelationships(ZipArchive archive, string relationshipPath)
    {
        var relationships = XlsxPackageTestFixtures
            .LoadPackageXml(archive, relationshipPath)
            .Root!
            .Elements(PackageRelNs + "Relationship")
            .ToList();
        relationships
            .Select(element => element.Attribute("Id")?.Value)
            .OfType<string>()
            .Should()
            .OnlyHaveUniqueItems();
        return relationships;
    }

    private static string AssertRelationshipRebound(
        List<XElement> relationships,
        string collidingId,
        string relationshipType,
        string target)
    {
        var relationship = relationships.Single(element =>
            (string?)element.Attribute("Type") == relationshipType &&
            (string?)element.Attribute("Target") == target);
        var id = relationship.Attribute("Id")!.Value;
        id.Should().NotBe(collidingId);
        return id;
    }

    private static void AssertSlicerTimelineContentTypes(ZipArchive archive)
    {
        var overrides = XlsxPackageTestFixtures
            .LoadPackageXml(archive, "[Content_Types].xml")
            .Root!
            .Elements(ContentTypeNs + "Override")
            .ToList();

        overrides.Should().ContainSingle(element =>
            (string?)element.Attribute("PartName") == "/xl/slicers/slicer1.xml" &&
            (string?)element.Attribute("ContentType") == "application/vnd.ms-excel.slicer+xml");
        overrides.Should().ContainSingle(element =>
            (string?)element.Attribute("PartName") == "/xl/slicerCaches/slicerCache1.xml" &&
            (string?)element.Attribute("ContentType") == "application/vnd.ms-excel.slicerCache+xml");
        overrides.Should().ContainSingle(element =>
            (string?)element.Attribute("PartName") == "/xl/timelines/timeline1.xml" &&
            (string?)element.Attribute("ContentType") == "application/vnd.ms-excel.Timeline+xml");
        overrides.Should().ContainSingle(element =>
            (string?)element.Attribute("PartName") == "/xl/timelineCaches/timelineCache1.xml" &&
            (string?)element.Attribute("ContentType") == "application/vnd.ms-excel.TimelineCache+xml");
    }

    private static MemoryStream CreateSlicerTimelineSourcePackage() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/slicers/slicer1.xml" ContentType="application/vnd.ms-excel.slicer+xml"/>
                  <Override PartName="/xl/slicerCaches/slicerCache1.xml" ContentType="application/vnd.ms-excel.slicerCache+xml"/>
                  <Override PartName="/xl/timelines/timeline1.xml" ContentType="application/vnd.ms-excel.Timeline+xml"/>
                  <Override PartName="/xl/timelineCaches/timelineCache1.xml" ContentType="application/vnd.ms-excel.TimelineCache+xml"/>
                </Types>
                """),
            ("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                          xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                          xmlns:x15="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main">
                  <sheets/>
                  <extLst>
                    <ext uri="{BBE1A952-AA13-448E-AADC-164F8A28A991}">
                      <x14:slicerCaches>
                        <x14:slicerCache r:id="rIdSlicerCache"/>
                      </x14:slicerCaches>
                    </ext>
                    <ext uri="{D0CA8CA8-9F24-4464-BF8E-62219DCF47F9}">
                      <x15:timelineCacheRefs>
                        <x15:timelineCacheRef r:id="rIdTimelineCache"/>
                      </x15:timelineCacheRefs>
                    </ext>
                  </extLst>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdSlicerCache"
                                Type="http://schemas.microsoft.com/office/2007/relationships/slicerCache"
                                Target="slicerCaches/slicerCache1.xml"/>
                  <Relationship Id="rIdTimelineCache"
                                Type="http://schemas.microsoft.com/office/2010/relationships/TimelineCache"
                                Target="timelineCaches/timelineCache1.xml"/>
                </Relationships>
                """),
            ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                           xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                           xmlns:x15="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main">
                  <sheetData/>
                  <extLst>
                    <ext uri="{A8765BA9-456A-4DAB-B4F3-ACF838C121DE}">
                      <x14:slicerList>
                        <x14:slicer r:id="rIdSlicer"/>
                      </x14:slicerList>
                    </ext>
                    <ext uri="{7E03D99C-DC04-49D9-9315-930204A7B6E9}">
                      <x15:timelineRefs>
                        <x15:timelineRef r:id="rIdTimeline"/>
                      </x15:timelineRefs>
                    </ext>
                  </extLst>
                </worksheet>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdSlicer"
                                Type="http://schemas.microsoft.com/office/2007/relationships/slicer"
                                Target="../slicers/slicer1.xml"/>
                  <Relationship Id="rIdTimeline"
                                Type="http://schemas.microsoft.com/office/2010/relationships/Timeline"
                                Target="../timelines/timeline1.xml"/>
                </Relationships>
                """),
            ("xl/slicers/slicer1.xml", """
                <slicers xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">
                  <slicer name="Region Slicer" cache="Slicer_Region"/>
                </slicers>
                """),
            ("xl/slicers/_rels/slicer1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdSlicerPartCache"
                                Type="http://schemas.microsoft.com/office/2007/relationships/slicerCache"
                                Target="../slicerCaches/slicerCache1.xml"/>
                </Relationships>
                """),
            ("xl/slicerCaches/slicerCache1.xml", """
                <slicerCacheDefinition xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main" name="Slicer_Region"/>
                """),
            ("xl/timelines/timeline1.xml", """
                <timelines xmlns="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main">
                  <timeline name="Date Timeline" cache="Timeline_Date"/>
                </timelines>
                """),
            ("xl/timelines/_rels/timeline1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdTimelinePartCache"
                                Type="http://schemas.microsoft.com/office/2010/relationships/TimelineCache"
                                Target="../timelineCaches/timelineCache1.xml"/>
                </Relationships>
                """),
            ("xl/timelineCaches/timelineCache1.xml", """
                <timelineCacheDefinition xmlns="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main" name="Timeline_Date"/>
                """));

    private static MemoryStream CreateGeneratedSlicerTimelineTargetPackage() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            ("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                          xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                          xmlns:x15="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main">
                  <sheets/>
                  <extLst>
                    <ext uri="{BBE1A952-AA13-448E-AADC-164F8A28A991}">
                      <x14:slicerCaches>
                        <x14:slicerCache r:id="rIdSlicerCache"/>
                      </x14:slicerCaches>
                    </ext>
                    <ext uri="{D0CA8CA8-9F24-4464-BF8E-62219DCF47F9}">
                      <x15:timelineCacheRefs>
                        <x15:timelineCacheRef r:id="rIdTimelineCache"/>
                      </x15:timelineCacheRefs>
                    </ext>
                  </extLst>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdSlicerCache"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"
                                Target="styles.xml"/>
                  <Relationship Id="rIdTimelineCache"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme"
                                Target="theme/theme1.xml"/>
                </Relationships>
                """),
            ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                           xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                           xmlns:x15="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main">
                  <sheetData/>
                  <extLst>
                    <ext uri="{A8765BA9-456A-4DAB-B4F3-ACF838C121DE}">
                      <x14:slicerList>
                        <x14:slicer r:id="rIdSlicer"/>
                      </x14:slicerList>
                    </ext>
                    <ext uri="{7E03D99C-DC04-49D9-9315-930204A7B6E9}">
                      <x15:timelineRefs>
                        <x15:timelineRef r:id="rIdTimeline"/>
                      </x15:timelineRefs>
                    </ext>
                  </extLst>
                </worksheet>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdSlicer"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"
                                Target="https://example.com/slicer"
                                TargetMode="External"/>
                  <Relationship Id="rIdTimeline"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"
                                Target="https://example.com/timeline"
                                TargetMode="External"/>
                </Relationships>
                """),
            ("xl/slicers/slicer1.xml", "<slicers xmlns=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/main\"/>"),
            ("xl/slicers/_rels/slicer1.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"/>"),
            ("xl/slicerCaches/slicerCache1.xml", "<slicerCacheDefinition xmlns=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/main\" name=\"GeneratedSlicer\"/>"),
            ("xl/timelines/timeline1.xml", "<timelines xmlns=\"http://schemas.microsoft.com/office/spreadsheetml/2010/11/main\"/>"),
            ("xl/timelines/_rels/timeline1.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"/>"),
            ("xl/timelineCaches/timelineCache1.xml", "<timelineCacheDefinition xmlns=\"http://schemas.microsoft.com/office/spreadsheetml/2010/11/main\" name=\"GeneratedTimeline\"/>"),
            ("xl/styles.xml", "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"/>"),
            ("xl/theme/theme1.xml", "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Office\"/>"));
}
