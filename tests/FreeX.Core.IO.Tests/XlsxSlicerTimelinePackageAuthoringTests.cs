using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxSlicerTimelinePackageAuthoringTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace MarkupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace SlicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    [Fact]
    public void ResolvePivotHostTabId_UsesModelOwnerAndPackageSheetId_WithLegacyFallback()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Cover");
        var data = workbook.AddSheet("Data");
        data.PivotTables.Add(new PivotTableModel { Name = "SalesPivot" });
        var workbookXml = XDocument.Parse(
            """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheets>
                <sheet name="Cover" sheetId="4" />
                <sheet name="DATA" sheetId="27" />
              </sheets>
            </workbook>
            """);

        XlsxSlicerTimelinePackageAuthoring.ResolvePivotHostTabId(workbook, workbookXml, "salespivot")
            .Should().Be("27");
        XlsxSlicerTimelinePackageAuthoring.ResolvePivotHostTabId(workbook, workbookXml, "Missing")
            .Should().Be("1");
        XlsxSlicerTimelinePackageAuthoring.ResolvePivotHostTabId(workbook, workbookXml, null)
            .Should().Be("1");
    }

    [Fact]
    public void RelationshipAuthoring_PreservesExistingRelationshipsAndDeduplicatesTargets()
    {
        using var package = CreatePackage(
            (
                "xl/worksheets/sheet7.xml",
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData /></worksheet>"),
            (
                "xl/worksheets/_rels/sheet7.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId9" Type="urn:existing" Target="../drawings/drawing1.xml" />
                </Relationships>
                """));
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);

        const string relationshipType = "urn:slicer";
        var firstId = XlsxSlicerTimelinePackageAuthoring.EnsureWorksheetRelationship(
            archive,
            "xl/worksheets/sheet7.xml",
            "xl/slicers/slicer3.xml",
            relationshipType);
        var secondId = XlsxSlicerTimelinePackageAuthoring.EnsureWorksheetRelationship(
            archive,
            "xl/worksheets/sheet7.xml",
            "xl/slicers/slicer3.xml",
            relationshipType);
        XlsxSlicerTimelinePackageAuthoring.EnsurePartRelationship(
            archive,
            "xl/slicers/slicer3.xml",
            "xl/slicerCaches/slicerCache3.xml",
            "urn:slicer-cache");

        secondId.Should().Be(firstId);
        var worksheetRelationships = Load(archive, "xl/worksheets/_rels/sheet7.xml.rels");
        worksheetRelationships.Root!.Elements(PackageRelNs + "Relationship").Should().HaveCount(2);
        worksheetRelationships.Root!.Elements(PackageRelNs + "Relationship")
            .Single(element => element.Attribute("Type")?.Value == relationshipType)
            .Attribute("Target")!.Value.Should().Be("../slicers/slicer3.xml");

        var slicerRelationships = Load(archive, "xl/slicers/_rels/slicer3.xml.rels");
        slicerRelationships.Root!.Elements(PackageRelNs + "Relationship").Should().ContainSingle()
            .Which.Attribute("Target")!.Value.Should().Be("../slicerCaches/slicerCache3.xml");
    }

    [Fact]
    public void ExtensionAuthoring_ReusesUriInPlaceAndPreservesExtensionOrder()
    {
        const string canonicalUri = "{A8765BA9-456A-4DAB-B4F3-ACF838C121DE}";
        var workbookXml = XDocument.Parse(
            """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <extLst>
                <ext uri="{UNRELATED}" />
                <ext uri="{a8765ba9-456a-4dab-b4f3-acf838c121de}" />
              </extLst>
            </workbook>
            """);

        XlsxSlicerTimelinePackageAuthoring.EnsureWorkbookExtensionRef(
            workbookXml,
            SlicerNs,
            "x14",
            canonicalUri,
            "slicerCaches",
            "slicerCache",
            "rId12");
        XlsxSlicerTimelinePackageAuthoring.EnsureWorkbookExtensionRef(
            workbookXml,
            SlicerNs,
            "x14",
            canonicalUri,
            "slicerCaches",
            "slicerCache",
            "rId12");

        var root = workbookXml.Root!;
        root.Attribute(MarkupCompatNs + "Ignorable")!.Value.Should().Be("x14");
        var extensions = root.Element(WorkbookNs + "extLst")!.Elements(WorkbookNs + "ext").ToList();
        extensions.Select(element => element.Attribute("uri")!.Value)
            .Should().Equal("{UNRELATED}", canonicalUri);
        extensions[1].Element(SlicerNs + "slicerCaches")!.Elements(SlicerNs + "slicerCache")
            .Should().ContainSingle()
            .Which.Attribute(RelNs + "id")!.Value.Should().Be("rId12");
    }

    [Fact]
    public void WriterAndStateRewriter_DelegateSharedPackageAuthoring()
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
            source.Should().Contain("XlsxSlicerTimelinePackageAuthoring.EnsurePartRelationship(", file);
            source.Should().Contain("XlsxSlicerTimelinePackageAuthoring.EnsureWorksheetRelationship(", file);
            source.Should().Contain("XlsxSlicerTimelinePackageAuthoring.EnsureWorkbookExtensionRef(", file);
            source.Should().Contain("XlsxSlicerTimelinePackageAuthoring.EnsureWorksheetExtensionRef(", file);
            source.Should().NotContain("private static string ResolvePivotHostTabId(", file);
            source.Should().NotContain("private static void EnsurePartRelationship(", file);
            source.Should().NotContain("private static string EnsureWorksheetRelationship(", file);
            source.Should().NotContain("private static void EnsureWorkbookExtensionRef(", file);
            source.Should().NotContain("private static void EnsureWorksheetExtensionRef(", file);
        }

        var xmlAuthoringSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.Core.IO",
            "XlsxSlicerTimelineXmlAuthoring.cs"));
        xmlAuthoringSource.Should().Contain("XlsxSlicerTimelinePackageAuthoring.ResolvePivotHostTabId(");
        xmlAuthoringSource.Should().NotContain("private static string ResolvePivotHostTabId(");
    }

    private static MemoryStream CreatePackage(params (string Path, string Xml)[] entries)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, xml) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(xml);
            }
        }

        package.Position = 0;
        return package;
    }

    private static XDocument Load(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        return XDocument.Load(stream);
    }
}
