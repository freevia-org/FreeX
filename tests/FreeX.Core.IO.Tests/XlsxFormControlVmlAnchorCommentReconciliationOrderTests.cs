using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R112-io-formcontrol-vml-anchor-comment-reorder-1: on a worksheet that has BOTH a legacy Form
/// Control (checkbox/button/etc.) and at least one cell Note, both kinds of VML shape live in the
/// same single shared <c>legacyDrawing</c> VML part. <see cref="XlsxFileAdapter"/>'s source-package
/// preservation pipeline runs <see cref="XlsxWorksheetFormControlPreserver"/>.Preserve (which patches
/// the control's shape's ClientData Anchor in place to reflect a row/column shift) BEFORE
/// <see cref="XlsxLegacyCommentPreserver"/>.Preserve -- and the comment preserver unconditionally
/// rebuilds the WHOLE VML document from the pristine SOURCE archive's copy of the part whenever the
/// sheet has any Notes (see its own doc comments on <c>PreserveReconciledVmlDrawing</c>), even when
/// nothing about the comments themselves changed. That rebuild keeps every non-Note shape --
/// including the form control's shape -- verbatim from the pristine snapshot, silently reverting the
/// anchor sync the form-control preserver had just written moments earlier.
///
/// These tests build a package with BOTH a real ClosedXML-authored Note (round-tripped through
/// FreeX's own writer via <see cref="Sheet.Comments"/>) and a hand-injected checkbox Form Control
/// sharing that same VML part -- FreeX has no in-model writer path for AUTHORING a form control from
/// scratch (only round-tripping one that already exists in a loaded source package), so the control's
/// XML/VML/ctrlProp nodes are hand-added on top of a real Save() output, mirroring the same
/// established hybrid approach already used by <see cref="XlsxFormControlVmlAnchorSyncTests"/>. The
/// Note itself is never shifted -- only the control's Anchor is mutated (simulating
/// <c>RowColumnShiftHelpers.AddressState.ShiftFormControls</c> after a row insert) -- so the sole
/// difference from that sibling test is the ADDITIONAL presence of an untouched Note on the sheet,
/// which is exactly what triggers the comment reconciliation's unconditional VML rebuild.
/// </summary>
public sealed class XlsxFormControlVmlAnchorCommentReconciliationOrderTests
{
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace FcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void R112_SaveAfterShift_ControlAnchorSyncSurvives_WhenSheetAlsoHasAnUnchangedNote()
    {
        using var package = BuildPackageWithCheckBoxControlNoteAndSharedVml();
        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.Sheets[0];
        var control = sheet.FormControls.Single();

        // Sanity: the Note survived the initial load untouched, sharing the control's VML part.
        sheet.Comments.Should().ContainSingle().Which.Value.Should().Be("Original note text");

        var anchor = control.Anchor!.Value;
        anchor.Start.Row.Should().Be(5, "sanity: source anchor is B5:C6 (1-based)");
        anchor.Start.Col.Should().Be(2);
        anchor.End.Row.Should().Be(6);
        anchor.End.Col.Should().Be(3);

        // Simulate RowColumnShiftHelpers.AddressState.ShiftFormControls rewriting Anchor/AnchorOffsets
        // after a row insert above the control (B5:C6 -> B6:C7). The Note (at D2) is left completely
        // untouched -- this is the exact scenario the defect requires: a form-control-only shift on a
        // sheet that ALSO has a Note, so XlsxLegacyCommentPreserver's reconciliation runs (because
        // sheet.Comments.Count > 0) even though nothing about the comment itself changed.
        control.Anchor = new GridRange(
            new CellAddress(anchor.Start.Sheet, anchor.Start.Row + 1, anchor.Start.Col),
            new CellAddress(anchor.Start.Sheet, anchor.End.Row + 1, anchor.End.Col));
        var offsets = control.AnchorOffsets!;
        control.AnchorOffsets = new DrawingAnchorRange(
            offsets.From with { Row = offsets.From.Row + 1 },
            offsets.To with { Row = offsets.To.Row + 1 });

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var vmlPath = ResolveVmlPath(archive);
        var vmlEntry = archive.GetEntry(vmlPath);
        vmlEntry.Should().NotBeNull("the shared VML drawing part must still round-trip");
        var vmlXml = XDocument.Load(vmlEntry!.Open());
        var controlShape = vmlXml.Root!.Descendants(VmlNs + "shape")
            .Single(s => (s.Attribute("id")?.Value ?? "").EndsWith("s1025", StringComparison.Ordinal));
        var anchorText = controlShape.Element(ExcelVmlNs + "ClientData")!.Element(ExcelVmlNs + "Anchor")!.Value;

        // leftCol=1,leftColOff=0,topRow=5,topRowOff=0,rightCol=2,rightColOff=0,bottomRow=6,bottomRowOff=0
        // (0-based: B6:C7 -> col1..2, row5..6), replacing the stale pre-shift "1,0,4,0,2,0,5,0".
        // BEFORE THE FIX: XlsxLegacyCommentPreserver's VML rebuild (running after the form-control
        // preserver, because the sheet also has a Note) reloads the pristine SOURCE VML and keeps the
        // control's shape verbatim from it, so this assertion fails with the STALE "1,0,4,0,2,0,5,0".
        anchorText.Should().Be("1,0,5,0,2,0,6,0",
            "the shared VML part's control ClientData Anchor must be rewritten to the shifted " +
            "position even when the sheet also has an untouched Note, or Excel renders the control " +
            "at its stale pre-shift location");
    }

    [Fact]
    public void R112_SaveAfterShift_NoteShapeAndTextSurviveUnchanged_AlongsideTheControlAnchorSync()
    {
        // Sibling/no-regression coverage: the Note's own shape and comment text must still be
        // preserved byte-faithfully in the SAME save that fixes the control's anchor sync above --
        // the fix must not degrade the comment preserver's own reconciliation of the shared VML part.
        using var package = BuildPackageWithCheckBoxControlNoteAndSharedVml();
        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.Sheets[0];
        var control = sheet.FormControls.Single();

        var anchor = control.Anchor!.Value;
        control.Anchor = new GridRange(
            new CellAddress(anchor.Start.Sheet, anchor.Start.Row + 1, anchor.Start.Col),
            new CellAddress(anchor.Start.Sheet, anchor.End.Row + 1, anchor.End.Col));
        var offsets = control.AnchorOffsets!;
        control.AnchorOffsets = new DrawingAnchorRange(
            offsets.From with { Row = offsets.From.Row + 1 },
            offsets.To with { Row = offsets.To.Row + 1 });

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var vmlEntry = archive.GetEntry(ResolveVmlPath(archive))!;
            var vmlXml = XDocument.Load(vmlEntry.Open());
            var noteShape = vmlXml.Root!.Descendants(VmlNs + "shape")
                .Single(s => s.Elements(ExcelVmlNs + "ClientData")
                    .Any(cd => string.Equals(cd.Attribute("ObjectType")?.Value, "Note", StringComparison.OrdinalIgnoreCase)));
            noteShape.Element(ExcelVmlNs + "ClientData")!.Element(ExcelVmlNs + "Row")!.Value
                .Should().Be("1", "the Note's own ClientData Row (0-based D2) must round-trip unchanged");
            noteShape.Element(ExcelVmlNs + "ClientData")!.Element(ExcelVmlNs + "Column")!.Value
                .Should().Be("3", "the Note's own ClientData Column (0-based D2) must round-trip unchanged");
        }

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedComment = reloaded.Sheets[0].Comments.Single();
        reloadedComment.Value.Should().Be("Original note text",
            "the Note's own text must still round-trip byte-faithfully in the same save");
    }

    private static MemoryStream BuildPackageWithCheckBoxControlNoteAndSharedVml()
    {
        var workbook = new Workbook("T");
        var sheet = workbook.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 9), new BoolValue(false)); // I4 linked cell

        // A genuine cell Note authored through the model, so ClosedXML (via FreeX's own Save path)
        // writes the comments1.xml part AND its own VML note shape into vmlDrawing1.vml for real --
        // this half of the fixture is a true round trip through our own writer, not hand-authored XML.
        var noteAddress = new CellAddress(sheet.Id, 2, 4); // D2
        sheet.Comments[noteAddress] = "Original note text";

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

            // ClosedXML already wrote its own <legacyDrawing r:id="..."/> marker (for the Note's VML)
            // -- reuse it (don't add a second one) since Excel only ever allows a single legacyDrawing
            // marker per worksheet, shared by both Notes and Form Controls.
            var existingLegacyDrawing = root.Element(WorkbookNs + "legacyDrawing");
            existingLegacyDrawing.Should().NotBeNull("ClosedXML must have written the Note's own legacyDrawing marker");
            var existingVmlRelId = existingLegacyDrawing!.Attribute(RelNs + "id")!.Value;

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
                          <control shapeId="1025" r:id="rIdCtrl" name="Check Box 1">
                            <controlPr defaultSize="0" autoFill="0" autoLine="0" autoPict="0">
                              <anchor>
                                <from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>4</xdr:row><xdr:rowOff>0</xdr:rowOff></from>
                                <to><xdr:col>2</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>5</xdr:row><xdr:rowOff>0</xdr:rowOff></to>
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

            var ctrlPropXml = new XDocument(new XElement(FcNs + "formControlPr",
                new XAttribute("objectType", "CheckBox"),
                new XAttribute("checked", "Unchecked"),
                new XAttribute("lockText", "1"),
                new XAttribute("noThreeD", "1"),
                new XAttribute("fmlaLink", "$I$4")));
            ReplaceEntry(archive, "xl/ctrlProps/ctrlProp1.xml", ctrlPropXml);

            // Resolve the VML path ClosedXML's own legacyDrawing marker already points at, and APPEND
            // the checkbox's shape into that SAME part alongside the Note's own shape -- this is the
            // crux of the defect scenario: both shapes must share one physical VML part.
            var relsPath = "xl/worksheets/_rels/" + Path.GetFileName(worksheetPath) + ".rels";
            var relsEntry = archive.GetEntry(relsPath)!;
            XDocument relsXml;
            using (var read = relsEntry.Open())
                relsXml = XDocument.Load(read);
            var vmlTarget = relsXml.Root!.Elements(PackageRelNs + "Relationship")
                .Single(r => string.Equals(r.Attribute("Id")?.Value, existingVmlRelId, StringComparison.Ordinal))
                .Attribute("Target")!.Value;
            var vmlPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, vmlTarget);

            var vmlEntry = archive.GetEntry(vmlPath)!;
            XDocument vmlXml;
            using (var read = vmlEntry.Open())
                vmlXml = XDocument.Load(read);

            // VML shape geometry matches the pre-shift controlPr anchor exactly: col1,off0,row4,off0
            // (from) .. col2,off0,row5,off0 (to), i.e. "1,0,4,0,2,0,5,0".
            var controlShape = XElement.Parse(
                "<v:shape xmlns:v=\"urn:schemas-microsoft-com:vml\" " +
                "xmlns:x=\"urn:schemas-microsoft-com:office:excel\" " +
                "id=\"_x0000_s1025\" type=\"#_x0000_t201\" " +
                "style=\"position:absolute;margin-left:36pt;margin-top:57.75pt;width:73.5pt;height:15.75pt;z-index:1\" filled=\"f\">" +
                "<v:textbox><div style=\"text-align:left\">Check Box 1</div></v:textbox>" +
                "<x:ClientData ObjectType=\"Checkbox\">" +
                "<x:Anchor>1,0,4,0,2,0,5,0</x:Anchor><x:AutoFill>False</x:AutoFill><x:FmlaLink>$I$4</x:FmlaLink>" +
                "</x:ClientData></v:shape>");
            vmlXml.Root!.Add(controlShape);
            ReplaceEntry(archive, vmlPath, vmlXml);

            AddCtrlPropRelationshipAndContentTypes(archive, worksheetPath);
        }

        result.Position = 0;
        return result;
    }

    private static void AddCtrlPropRelationshipAndContentTypes(ZipArchive archive, string worksheetPath)
    {
        var relsPath = "xl/worksheets/_rels/" + Path.GetFileName(worksheetPath) + ".rels";
        var relsEntry = archive.GetEntry(relsPath)!;
        XDocument relsXml;
        using (var read = relsEntry.Open())
            relsXml = XDocument.Load(read);

        relsXml.Root!.Add(
            new XElement(PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdCtrl"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp"),
                new XAttribute("Target", "../ctrlProps/ctrlProp1.xml")));
        ReplaceEntry(archive, relsPath, relsXml);

        XNamespace ctNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var ctEntry = archive.GetEntry("[Content_Types].xml")!;
        XDocument ctXml;
        using (var read = ctEntry.Open())
            ctXml = XDocument.Load(read);
        var ctRoot = ctXml.Root!;
        ctRoot.Add(new XElement(ctNs + "Override",
            new XAttribute("PartName", "/xl/ctrlProps/ctrlProp1.xml"),
            new XAttribute("ContentType", "application/vnd.ms-excel.controlproperties+xml")));
        ReplaceEntry(archive, "[Content_Types].xml", ctXml);
    }

    /// <summary>
    /// Resolves the worksheet's current legacyDrawing VML part path via its own relationships,
    /// rather than assuming a fixed name/number -- ClosedXML names this part
    /// <c>xl/drawings/vmldrawing.vml</c> (no number, all-lowercase "drawing"), which is easy to get
    /// wrong by hardcoding a guessed path.
    /// </summary>
    private static string ResolveVmlPath(ZipArchive archive)
    {
        var worksheetEntry = archive.Entries.Single(e =>
            e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        var worksheetPath = worksheetEntry.FullName;

        XDocument worksheetXml;
        using (var read = worksheetEntry.Open())
            worksheetXml = XDocument.Load(read);
        var vmlRelId = worksheetXml.Root!.Element(WorkbookNs + "legacyDrawing")!.Attribute(RelNs + "id")!.Value;

        var relsPath = "xl/worksheets/_rels/" + Path.GetFileName(worksheetPath) + ".rels";
        var relsEntry = archive.GetEntry(relsPath)!;
        XDocument relsXml;
        using (var read = relsEntry.Open())
            relsXml = XDocument.Load(read);
        var target = relsXml.Root!.Elements(PackageRelNs + "Relationship")
            .Single(r => string.Equals(r.Attribute("Id")?.Value, vmlRelId, StringComparison.Ordinal))
            .Attribute("Target")!.Value;
        return XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
    }

    private static void ReplaceEntry(ZipArchive archive, string path, XDocument xml)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        xml.Save(stream, SaveOptions.DisableFormatting);
    }
}
