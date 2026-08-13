using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R40-io-vml-shape-geometry-3-3: the fallback restoration of a dropped
/// <c>&lt;legacyDrawing&gt;</c> marker (<see cref="XlsxWorksheetMetadataPreserver"/>'s
/// <c>CreateReboundRetainedWorksheetBlock</c>) previously only performed the collision-aware
/// relationship rebind for the <c>&lt;picture&gt;</c> case, leaving a restored
/// <c>&lt;legacyDrawing&gt;</c> carrying the SOURCE package's raw <c>r:id</c> verbatim. When the
/// regenerated worksheet's own relationships already occupy that id (a common collision, since
/// worksheet-local rel ids are independently allocated), <see cref="XlsxPackageMetadataMerger.MergeRelationshipParts"/>
/// remaps the incoming vmlDrawing relationship to a new id — but the copied marker still points at
/// the stale, now-wrong id, orphaning the VML shape (e.g. an OLE-object preview icon: a VML shape
/// that is neither a modeled comment nor a form control, so neither of those preservers ever touch
/// this sheet's legacyDrawing).
///
/// This test forces exactly that collision (the source's vmlDrawing relationship id already being
/// used by an unrelated relationship in the regenerated target) and asserts the restored marker's
/// r:id is rebound to whatever id the merge actually assigned, resolving to the vmlDrawing part —
/// not the stale source id, which after the merge points at the wrong relationship.
/// </summary>
public sealed class XlsxWorksheetMetadataPreserverLegacyDrawingRebindTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void RestoredLegacyDrawingMarker_RebindsRelationshipIdWhenItCollidesWithTargetsOwnRelationship()
    {
        using var sourcePackage = CreateSourcePackage();
        using var targetPackage = CreateTargetPackageWithCollidingRelationshipId();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        // Mirror the real save pipeline's ordering (XlsxFileAdapter.SourcePackage.cs): unknown parts +
        // relationships are merged (and collision-remapped) BEFORE XlsxWorksheetMetadataPreserver runs.
        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeContentTypes(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);

        // Sanity: the collision actually forced a remap (the vmlDrawing relationship the merger just
        // added must NOT still be sitting at the source's original "rIdVml" id, since that id was
        // already taken by the target's own hyperlink relationship).
        var relsAfterMerge = XlsxPackageTestFixtures.LoadPackageXml(targetArchive, "xl/worksheets/_rels/sheet1.xml.rels");
        var vmlRelationshipAfterMerge = relsAfterMerge.Root!
            .Elements(PackageRelNs + "Relationship")
            .Single(e => (string?)e.Attribute("Type") ==
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing");
        var reboundVmlRelationshipId = vmlRelationshipAfterMerge.Attribute("Id")!.Value;
        reboundVmlRelationshipId.Should().NotBe("rIdVml",
            "sanity: the collision with the target's pre-existing rIdVml hyperlink must force the merger to remap the incoming vmlDrawing relationship");

        var workbook = new Workbook("T");
        workbook.AddSheet("Data");
        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive);
        context.Should().NotBeNull();

        XlsxWorksheetMetadataPreserver.Preserve(workbook, context);

        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(targetArchive, "xl/worksheets/sheet1.xml");
        var legacyDrawing = worksheetXml.Root!.Element(WorkbookNs + "legacyDrawing");
        legacyDrawing.Should().NotBeNull("the unmodeled legacyDrawing marker must still be restored");

        var restoredRelId = legacyDrawing!.Attribute(RelNs + "id")!.Value;
        restoredRelId.Should().Be(reboundVmlRelationshipId,
            "the restored <legacyDrawing> marker's r:id must be rebound to the id the merge actually assigned, " +
            "not copied verbatim from the source package");
        restoredRelId.Should().NotBe("rIdVml",
            "the stale source r:id now resolves to the target's own (unrelated) hyperlink relationship, not the vmlDrawing part");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Source: an unmodeled VML shape (neither a comment nor a form control) behind <legacyDrawing>.
    // ─────────────────────────────────────────────────────────────────────────

    private static MemoryStream CreateSourcePackage() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="vml" ContentType="application/vnd.openxmlformats-officedocument.vmlDrawing"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            ("_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            ("xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """),
            ("xl/worksheets/sheet1.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1"/>
                  <sheetData/>
                  <legacyDrawing r:id="rIdVml"/>
                </worksheet>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdVml" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
                </Relationships>
                """),
            ("xl/drawings/vmlDrawing1.vml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <xml xmlns:v="urn:schemas-microsoft-com:vml"
                     xmlns:o="urn:schemas-microsoft-com:office:office">
                  <v:shape id="_x0000_s2048" type="#_x0000_t75" style="position:absolute;visibility:hidden" o:insetmode="auto">
                    <v:imagedata o:title="OLE preview"/>
                  </v:shape>
                </xml>
                """));

    // ─────────────────────────────────────────────────────────────────────────
    // Target: the ClosedXML-regenerated worksheet, whose own hyperlink relationship happens to
    // already occupy "rIdVml" — forcing MergeRelationshipParts to remap the incoming vmlDrawing rel.
    // ─────────────────────────────────────────────────────────────────────────

    private static MemoryStream CreateTargetPackageWithCollidingRelationshipId() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            ("_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            ("xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """),
            ("xl/worksheets/sheet1.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1"/>
                  <sheetData/>
                </worksheet>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdVml" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.com/unrelated" TargetMode="External"/>
                </Relationships>
                """));
}
