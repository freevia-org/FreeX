using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R115-io-deleted-form-control-1: when a row/column delete fully removes a
/// legacy form control's anchor, <c>RowColumnShiftHelpers.AddressState.ShiftFormControls</c> correctly
/// drops the control from <see cref="Sheet.FormControls"/> in memory (mirroring Excel's own behavior
/// of deleting the shape outright), but <see cref="XlsxWorksheetFormControlPreserver"/> previously
/// cloned the source worksheet's ENTIRE <c>&lt;controls&gt;</c> container verbatim and exited
/// <c>ApplyControlAnchorsToClone</c> immediately when the sheet had no live controls left, so the
/// deleted control's <c>&lt;control&gt;</c> element, ctrlProp part, and VML shape all survived
/// untouched in the saved package -- Excel would resurrect the "deleted" shape on reopen. When the
/// sheet had MULTIPLE controls, the bug was worse: both <c>WriteControlStateToCtrlProps</c> and
/// <c>ApplyControlAnchorsToClone</c> fell back to positional indexing whenever a shapeId lookup
/// missed (which it always did for the orphaned element), wrongly binding a SURVIVING control's live
/// anchor/state onto the orphaned element -- producing a duplicate, overlapping shape at the
/// survivor's position instead of the deleted control disappearing.
///
/// These tests build a package with form controls the same way <see cref="XlsxFormControlShiftPersistenceTests"/>
/// and <see cref="XlsxFormControlVmlAnchorSyncTests"/> do (loaded through the real
/// <see cref="XlsxFileAdapter"/>, using the identical outer-<c>mc:AlternateContent</c>-wrapped
/// <c>&lt;controls&gt;</c> shape those sibling fixtures already establish as realistic), then simulate
/// <c>ShiftFormControls</c> dropping a deleted control from the live model the same way those sibling
/// test classes simulate a shift (mutating the loaded model directly, without depending on
/// FreeX.Core.Commands), and assert the save/reload round trip through the real
/// <see cref="XlsxFileAdapter"/> reflects the deletion -- while an untouched sheet's controls still
/// round-trip unchanged (no regression).
/// </summary>
public sealed class XlsxFormControlDeletedControlPersistenceTests
{
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace FcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";

    [Fact]
    public void R115_SingleControlDeleted_RemovedFromModelAndFile_NotResurrected()
    {
        // The simplest, most severe form of the bug: the sheet's ONLY control is deleted. Previously
        // ApplyControlAnchorsToClone returned immediately when sheet.FormControls.Count == 0, leaving
        // the stale <control>/ctrlProp/VML shape completely untouched.
        using var package = BuildPackageWithTwoCheckBoxControls();
        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.Sheets[0];
        sheet.FormControls.Should().HaveCount(2, "sanity: both controls loaded");

        // Simulate a row delete that removes BOTH controls' anchors (RowColumnShiftHelpers.
        // AddressState.ShiftFormControls drops each fully-deleted control from FormControls entirely).
        sheet.FormControls.Clear();

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.Sheets[0].FormControls.Should().BeEmpty(
            "both controls were deleted -- none should resurrect on reload");

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = XDocument.Load(archive.Entries
            .Single(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                         e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Open());
        worksheetXml.Descendants(WorksheetNs + "control").Should().BeEmpty(
            "the saved worksheet must not carry a stale <control> element for either deleted control");

        archive.GetEntry("xl/ctrlProps/ctrlProp1.xml").Should().BeNull(
            "the deleted control's ctrlProp part must not survive in the saved package");
        archive.GetEntry("xl/ctrlProps/ctrlProp2.xml").Should().BeNull(
            "the deleted control's ctrlProp part must not survive in the saved package");
    }

    [Fact]
    public void R115_OneOfTwoControlsDeleted_OrphanedElementRemoved_NotMisboundToSurvivor()
    {
        // The worse, multi-control form of the bug: control 1025 (document order FIRST) is deleted
        // while control 1026 (document order SECOND) survives untouched. Previously, the positional
        // fallback would bind control 1026's live anchor/state onto the ORPHANED shapeId=1025 element
        // (duplicating 1026's position) instead of removing that element outright.
        using var package = BuildPackageWithTwoCheckBoxControls();
        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.Sheets[0];
        var survivor = sheet.FormControls.Single(c => c.ShapeId == 1026);
        var survivorAnchor = survivor.Anchor!.Value;

        // Simulate ShiftFormControls dropping ONLY the deleted control (shapeId 1025) from the live
        // model, exactly as it does for a control whose anchor fell entirely within a deleted
        // row/column, leaving the surviving control (1026) exactly as loaded.
        sheet.FormControls.RemoveAll(c => c.ShapeId == 1025);
        sheet.FormControls.Should().HaveCount(1);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedControls = reloaded.Sheets[0].FormControls;
        reloadedControls.Should().HaveCount(1,
            "only the surviving control (shapeId 1026) should remain -- the deleted one (1025) must not resurrect, " +
            "whether at its own stale position or duplicated onto the survivor's position");
        var reloadedSurvivor = reloadedControls.Single();
        reloadedSurvivor.ShapeId.Should().Be(1026u);
        reloadedSurvivor.Anchor!.Value.Start.Row.Should().Be(survivorAnchor.Start.Row,
            "the surviving control's own anchor must be exactly what it was loaded with, not corrupted by the orphan cleanup");
        reloadedSurvivor.Anchor!.Value.Start.Col.Should().Be(survivorAnchor.Start.Col);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = XDocument.Load(archive.Entries
            .Single(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                         e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Open());
        var controlElements = worksheetXml.Descendants(WorksheetNs + "control").ToList();
        controlElements.Should().ContainSingle(
            "exactly one <control> element (shapeId 1026) must remain -- the orphaned shapeId=1025 element must be gone, " +
            "not merely re-bound to the survivor's position (which would leave TWO overlapping elements)");
        controlElements.Single().Attribute("shapeId")!.Value.Should().Be("1026");

        // The orphaned control's ctrlProp part must be gone; the survivor's must remain untouched.
        archive.GetEntry("xl/ctrlProps/ctrlProp1.xml").Should().BeNull(
            "the deleted control's ctrlProp part must be removed, not left as a dangling orphan");
        var survivorCtrlPropEntry = archive.GetEntry("xl/ctrlProps/ctrlProp2.xml");
        survivorCtrlPropEntry.Should().NotBeNull("the surviving control's ctrlProp part must still be present");
        var survivorCtrlPropXml = XDocument.Load(survivorCtrlPropEntry!.Open());
        survivorCtrlPropXml.Root!.Attribute("checked")!.Value.Should().Be("Unchecked",
            "the surviving control's own ctrlProp state must be untouched by the orphan's removal");

        // The orphaned control's VML shape must be gone; the survivor's VML shape must remain.
        var vmlEntry = archive.GetEntry("xl/drawings/vmlDrawing1.vml");
        vmlEntry.Should().NotBeNull();
        var vmlXml = XDocument.Load(vmlEntry!.Open());
        var shapeIds = vmlXml.Root!.Descendants(VmlNs + "shape")
            .Select(s => s.Attribute("id")?.Value ?? "")
            .ToList();
        shapeIds.Should().NotContain(id => id.EndsWith("s1025", StringComparison.Ordinal),
            "the deleted control's VML shape must be removed -- Excel renders legacy Form Controls from the VML " +
            "layer, so leaving this shape behind would keep the 'deleted' control fully visible");
        shapeIds.Should().Contain(id => id.EndsWith("s1026", StringComparison.Ordinal),
            "the surviving control's VML shape must remain");
    }

    [Fact]
    public void R115_NoDeletion_BothControlsRoundTripUnchanged()
    {
        // Negative control: saving without deleting anything must still preserve BOTH controls
        // exactly as loaded (no regression to the ordinary multi-control save path).
        using var package = BuildPackageWithTwoCheckBoxControls();
        var workbook = new XlsxFileAdapter().Load(package);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        var controls = reloaded.Sheets[0].FormControls;
        controls.Should().HaveCount(2, "no controls were deleted -- both must still round-trip");
        controls.Should().Contain(c => c.ShapeId == 1025);
        controls.Should().Contain(c => c.ShapeId == 1026);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        archive.GetEntry("xl/ctrlProps/ctrlProp1.xml").Should().NotBeNull();
        archive.GetEntry("xl/ctrlProps/ctrlProp2.xml").Should().NotBeNull();
        var vmlXml = XDocument.Load(archive.GetEntry("xl/drawings/vmlDrawing1.vml")!.Open());
        vmlXml.Root!.Descendants(VmlNs + "shape").Should().HaveCount(2,
            "both VML shapes must still be present when nothing was deleted");
    }

    private static MemoryStream BuildPackageWithTwoCheckBoxControls()
    {
        var workbook = new Workbook("T");
        var sheet = workbook.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 9), new BoolValue(false));   // I4 linked cell (control 1)
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new BoolValue(false));  // I9 linked cell (control 2)

        var baseStream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, baseStream);
        baseStream.Position = 0;

        var result = new MemoryStream();
        baseStream.CopyTo(result);
        result.Position = 0;

        using (var archive = new ZipArchive(result, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetEntry = archive.Entries.Single(e =>
                e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            var worksheetPath = worksheetEntry.FullName;

            XDocument worksheetXml;
            using (var read = worksheetEntry.Open())
                worksheetXml = XDocument.Load(read);
            var root = worksheetXml.Root!;
            root.SetAttributeValue(XNamespace.Xmlns + "r", RelNs.NamespaceName);
            root.Add(XElement.Parse(
                """
                <legacyDrawing xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                               xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                               r:id="rIdVml"/>
                """));
            // Mirrors the exact shape XlsxFormControlShiftPersistenceTests/XlsxFormControlVmlAnchorSyncTests
            // already use: the WHOLE <controls> block wrapped in one outer mc:AlternateContent, with each
            // individual <control> ALSO wrapped in its own inner mc:AlternateContent (Excel's actual x14
            // forward-compat shape for Forms-toolbar controls).
            root.Add(XElement.Parse(
                """
                <mc:AlternateContent xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                                     xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                                     xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                                     xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing">
                  <mc:Choice Requires="x14">
                    <controls>
                      <mc:AlternateContent xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006">
                        <mc:Choice Requires="x14">
                          <control shapeId="1025" r:id="rIdCtrl1" name="Check Box 1">
                            <controlPr defaultSize="0" autoFill="0" autoLine="0" autoPict="0">
                              <anchor>
                                <from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>4</xdr:row><xdr:rowOff>0</xdr:rowOff></from>
                                <to><xdr:col>2</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>5</xdr:row><xdr:rowOff>0</xdr:rowOff></to>
                              </anchor>
                            </controlPr>
                          </control>
                        </mc:Choice>
                      </mc:AlternateContent>
                      <mc:AlternateContent xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006">
                        <mc:Choice Requires="x14">
                          <control shapeId="1026" r:id="rIdCtrl2" name="Check Box 2">
                            <controlPr defaultSize="0" autoFill="0" autoLine="0" autoPict="0">
                              <anchor>
                                <from><xdr:col>4</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>9</xdr:row><xdr:rowOff>0</xdr:rowOff></from>
                                <to><xdr:col>5</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>10</xdr:row><xdr:rowOff>0</xdr:rowOff></to>
                              </anchor>
                            </controlPr>
                          </control>
                        </mc:Choice>
                      </mc:AlternateContent>
                    </controls>
                  </mc:Choice>
                </mc:AlternateContent>
                """));
            ReplaceEntry(archive, worksheetPath, worksheetXml);

            var ctrlProp1Xml = new XDocument(new XElement(FcNs + "formControlPr",
                new XAttribute("objectType", "CheckBox"),
                new XAttribute("checked", "Unchecked"),
                new XAttribute("lockText", "1"),
                new XAttribute("noThreeD", "1"),
                new XAttribute("fmlaLink", "$I$4")));
            ReplaceEntry(archive, "xl/ctrlProps/ctrlProp1.xml", ctrlProp1Xml);

            var ctrlProp2Xml = new XDocument(new XElement(FcNs + "formControlPr",
                new XAttribute("objectType", "CheckBox"),
                new XAttribute("checked", "Unchecked"),
                new XAttribute("lockText", "1"),
                new XAttribute("noThreeD", "1"),
                new XAttribute("fmlaLink", "$I$9")));
            ReplaceEntry(archive, "xl/ctrlProps/ctrlProp2.xml", ctrlProp2Xml);

            var vml =
                "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" " +
                "xmlns:x=\"urn:schemas-microsoft-com:office:excel\">" +
                "<v:shape id=\"_x0000_s1025\" type=\"#_x0000_t201\" " +
                "style=\"position:absolute;margin-left:36pt;margin-top:57.75pt;width:73.5pt;height:15.75pt;z-index:1\" filled=\"f\">" +
                "<v:textbox><div style=\"text-align:left\">Check Box 1</div></v:textbox>" +
                "<x:ClientData ObjectType=\"Checkbox\">" +
                "<x:Anchor>1,0,4,0,2,0,5,0</x:Anchor><x:AutoFill>False</x:AutoFill><x:FmlaLink>$I$4</x:FmlaLink>" +
                "</x:ClientData></v:shape>" +
                "<v:shape id=\"_x0000_s1026\" type=\"#_x0000_t201\" " +
                "style=\"position:absolute;margin-left:180pt;margin-top:129.75pt;width:73.5pt;height:15.75pt;z-index:2\" filled=\"f\">" +
                "<v:textbox><div style=\"text-align:left\">Check Box 2</div></v:textbox>" +
                "<x:ClientData ObjectType=\"Checkbox\">" +
                "<x:Anchor>4,0,9,0,5,0,10,0</x:Anchor><x:AutoFill>False</x:AutoFill><x:FmlaLink>$I$9</x:FmlaLink>" +
                "</x:ClientData></v:shape></xml>";
            ReplaceRawEntry(archive, "xl/drawings/vmlDrawing1.vml", vml);

            AddRelationshipsAndContentTypes(archive, worksheetPath);
        }

        result.Position = 0;
        return result;
    }

    private static void AddRelationshipsAndContentTypes(ZipArchive archive, string worksheetPath)
    {
        var relsPath = "xl/worksheets/_rels/" + Path.GetFileName(worksheetPath) + ".rels";
        XDocument relsXml;
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is not null)
        {
            using var read = relsEntry.Open();
            relsXml = XDocument.Load(read);
        }
        else
        {
            relsXml = new XDocument(new XElement(PackageRelNs + "Relationships"));
        }

        relsXml.Root!.Add(
            new XElement(PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdCtrl1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp"),
                new XAttribute("Target", "../ctrlProps/ctrlProp1.xml")),
            new XElement(PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdCtrl2"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp"),
                new XAttribute("Target", "../ctrlProps/ctrlProp2.xml")),
            new XElement(PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdVml"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing"),
                new XAttribute("Target", "../drawings/vmlDrawing1.vml")));
        ReplaceEntry(archive, relsPath, relsXml);

        XNamespace ctNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var ctEntry = archive.GetEntry("[Content_Types].xml")!;
        XDocument ctXml;
        using (var read = ctEntry.Open())
            ctXml = XDocument.Load(read);
        var ctRoot = ctXml.Root!;
        if (!ctRoot.Elements(ctNs + "Default").Any(d => string.Equals(d.Attribute("Extension")?.Value, "vml", StringComparison.OrdinalIgnoreCase)))
        {
            ctRoot.Add(new XElement(ctNs + "Default",
                new XAttribute("Extension", "vml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.vmlDrawing")));
        }

        ctRoot.Add(new XElement(ctNs + "Override",
            new XAttribute("PartName", "/xl/ctrlProps/ctrlProp1.xml"),
            new XAttribute("ContentType", "application/vnd.ms-excel.controlproperties+xml")));
        ctRoot.Add(new XElement(ctNs + "Override",
            new XAttribute("PartName", "/xl/ctrlProps/ctrlProp2.xml"),
            new XAttribute("ContentType", "application/vnd.ms-excel.controlproperties+xml")));
        ReplaceEntry(archive, "[Content_Types].xml", ctXml);
    }

    private static void ReplaceEntry(ZipArchive archive, string path, XDocument xml)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        xml.Save(stream, SaveOptions.DisableFormatting);
    }

    private static void ReplaceRawEntry(ZipArchive archive, string path, string content)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
