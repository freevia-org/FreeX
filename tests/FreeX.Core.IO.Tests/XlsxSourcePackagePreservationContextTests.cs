using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxSourcePackagePreservationContextTests
{
    [Fact]
    public void TryCreate_OwnsSharedPackageStateWithoutOwningArchiveLifetime()
    {
        using var sourcePackage = CreatePackage(includeWorkbookRelationships: true, includeWorksheetRelationships: true);
        using var targetPackage = CreatePackage(includeWorkbookRelationships: true, includeWorksheetRelationships: false);
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive);

        context.Should().NotBeNull();
        context!.SourceArchive.Should().BeSameAs(sourceArchive);
        context.TargetArchive.Should().BeSameAs(targetArchive);
        context.SourceSheets.Should().Contain("Sheet1", "xl/worksheets/sheet1.xml");
        context.TargetSheets.Should().Contain("Sheet1", "xl/worksheets/sheet1.xml");

        var firstWorksheetXml = context.GetSourceWorksheetXml("xl/worksheets/sheet1.xml");
        var secondWorksheetXml = context.GetSourceWorksheetXml("xl/worksheets/sheet1.xml");
        secondWorksheetXml.Should().BeSameAs(firstWorksheetXml);

        var firstTargets = context.GetSourceRelationshipTargets("xl/worksheets/sheet1.xml");
        var secondTargets = context.GetSourceRelationshipTargets("xl/worksheets/sheet1.xml");
        secondTargets.Should().BeSameAs(firstTargets);
        firstTargets.Should().Contain("rIdDrawing", "xl/drawings/drawing1.xml");

        targetArchive.CreateEntry("xl/session-remains-open.bin");
        sourceArchive.GetEntry("xl/workbook.xml").Should().NotBeNull();
        targetArchive.GetEntry("xl/session-remains-open.bin").Should().NotBeNull();
    }

    [Fact]
    public void ReplaceTargetWorkbookRelationships_RefreshesCurrentSheetPathMap()
    {
        using var sourcePackage = CreatePackage(includeWorkbookRelationships: true, includeWorksheetRelationships: false);
        using var targetPackage = CreatePackage(includeWorkbookRelationships: true, includeWorksheetRelationships: false);
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);
        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive)!;
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        WriteEntry(targetArchive, "xl/worksheets/renumbered.xml", WorksheetXml);
        var relationshipsXml = context.LoadCurrentTargetWorkbookRelationshipsXml();
        relationshipsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Single(relationship => relationship.Attribute("Id")?.Value == "rId1")
            .SetAttributeValue("Target", "worksheets/renumbered.xml");

        context.ReplaceTargetWorkbookRelationshipsXml(relationshipsXml, refreshSheetPaths: true);

        context.TargetSheets.Should().Contain("Sheet1", "xl/worksheets/renumbered.xml");
    }

    [Fact]
    public void TryCreate_IncompleteRelationshipGraphPreservesLegacyPartialPackageBehavior()
    {
        using var sourcePackage = CreatePackage(includeWorkbookRelationships: false, includeWorksheetRelationships: false);
        using var targetPackage = CreatePackage(includeWorkbookRelationships: false, includeWorksheetRelationships: false);
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive);

        context.Should().NotBeNull("workbook-only preservation such as pivot-cache ordering remains possible");
        context!.HasSourceWorkbookRelationshipsPart.Should().BeFalse();
        context.HasTargetWorkbookRelationshipsPart.Should().BeFalse();
        context.SourceSheets.Should().BeEmpty();
        context.TargetSheets.Should().BeEmpty();
        context.GetSourceRelationshipTargets("xl/worksheets/sheet1.xml").Should().BeEmpty();
    }

    [Fact]
    public void TryCreate_MissingWorkbookPartReturnsNull()
    {
        using var sourcePackage = new MemoryStream();
        using (var archive = new ZipArchive(sourcePackage, ZipArchiveMode.Create, leaveOpen: true))
            WriteEntry(archive, "xl/not-workbook.xml", "<placeholder />");
        sourcePackage.Position = 0;

        using var targetPackage = CreatePackage(includeWorkbookRelationships: true, includeWorksheetRelationships: false);
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive).Should().BeNull();
    }

    private static MemoryStream CreatePackage(bool includeWorkbookRelationships, bool includeWorksheetRelationships)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", WorkbookXml);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", WorksheetXml);
            if (includeWorkbookRelationships)
                WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml);
            if (includeWorksheetRelationships)
            {
                WriteEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", WorksheetRelationshipsXml);
                WriteEntry(archive, "xl/drawings/drawing1.xml", "<drawing />");
            }
        }

        package.Position = 0;
        return package;
    }

    private static void WriteEntry(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(text);
    }

    private const string WorkbookXml = """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets><sheet name="Sheet1" sheetId="1" r:id="rId1" /></sheets>
        </workbook>
        """;

    private const string WorkbookRelationshipsXml = """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1"
                        Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                        Target="worksheets/sheet1.xml" />
        </Relationships>
        """;

    private const string WorksheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData />
        </worksheet>
        """;

    private const string WorksheetRelationshipsXml = """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdDrawing"
                        Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"
                        Target="../drawings/drawing1.xml" />
        </Relationships>
        """;
}
