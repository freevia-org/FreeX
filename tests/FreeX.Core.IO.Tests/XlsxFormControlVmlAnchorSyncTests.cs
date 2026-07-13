using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R40-io-vml-shape-geometry-3-1: a row/column shift correctly rewrites a
/// form control's modern <c>controlPr/anchor</c> (see <see cref="XlsxFormControlShiftPersistenceTests"/>)
/// but previously left the control's VML shape geometry stale, since the VML part is only ever
/// byte-copied verbatim by <see cref="XlsxPackageMetadataMerger.CopyUnknownPackageParts"/>. Legacy
/// Form Controls are still rendered by Excel's VML layer (not DrawingML), so the control visually
/// stayed at its pre-shift position in real Excel even though the modern anchor said it moved.
///
/// These tests build a package with a checkbox control whose VML shape carries an
/// <c>&lt;x:ClientData&gt;&lt;x:Anchor&gt;</c> matching its pre-shift <c>controlPr/anchor</c>, mutate
/// the loaded <see cref="FormControlModel"/>'s Anchor/AnchorOffsets the same way
/// <c>RowColumnShiftHelpers.AddressState.ShiftFormControls</c> would after a row insert, and assert
/// the saved VML shape's ClientData Anchor is rewritten to match — while an untouched control's VML
/// anchor still round-trips unchanged (no regression).
/// </summary>
public sealed class XlsxFormControlVmlAnchorSyncTests
{
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace FcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";

    [Fact]
    public void SaveAfterShift_CheckBox_RewritesVmlClientDataAnchorToShiftedPosition()
    {
        using var package = BuildPackageWithCheckBoxControlAndVml();
        var workbook = new XlsxFileAdapter().Load(package);
        var control = workbook.Sheets[0].FormControls.Single();

        var anchor = control.Anchor!.Value;
        anchor.Start.Row.Should().Be(5, "sanity: source anchor is B5:C6 (1-based)");
        anchor.Start.Col.Should().Be(2);
        anchor.End.Row.Should().Be(6);
        anchor.End.Col.Should().Be(3);

        // Simulate RowColumnShiftHelpers.AddressState.ShiftFormControls rewriting Anchor/AnchorOffsets
        // after a row insert above the control (B5:C6 -> B6:C7).
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
        var vmlEntry = archive.GetEntry("xl/drawings/vmlDrawing1.vml");
        vmlEntry.Should().NotBeNull("the VML drawing part must still round-trip");
        var vmlXml = XDocument.Load(vmlEntry!.Open());
        var shape = vmlXml.Root!.Descendants(VmlNs + "shape")
            .Single(s => (s.Attribute("id")?.Value ?? "").EndsWith("s1025", StringComparison.Ordinal));
        var anchorText = shape.Element(ExcelVmlNs + "ClientData")!.Element(ExcelVmlNs + "Anchor")!.Value;

        // leftCol=1,leftColOff=0,topRow=5,topRowOff=0,rightCol=2,rightColOff=0,bottomRow=6,bottomRowOff=0
        // (0-based: B6:C7 -> col1..2, row5..6), replacing the stale pre-shift "1,0,4,0,2,0,5,0".
        anchorText.Should().Be("1,0,5,0,2,0,6,0",
            "the VML shape's ClientData Anchor must be rewritten to the shifted position, " +
            "or Excel renders the control at its stale pre-shift location");
    }

    [Fact]
    public void SaveWithoutShift_CheckBox_VmlClientDataAnchorRoundTripsUnchanged()
    {
        // Negative control: an untouched control's VML anchor must still round-trip unchanged.
        using var package = BuildPackageWithCheckBoxControlAndVml();
        var workbook = new XlsxFileAdapter().Load(package);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var vmlEntry = archive.GetEntry("xl/drawings/vmlDrawing1.vml");
        vmlEntry.Should().NotBeNull();
        var vmlXml = XDocument.Load(vmlEntry!.Open());
        var shape = vmlXml.Root!.Descendants(VmlNs + "shape")
            .Single(s => (s.Attribute("id")?.Value ?? "").EndsWith("s1025", StringComparison.Ordinal));
        var anchorText = shape.Element(ExcelVmlNs + "ClientData")!.Element(ExcelVmlNs + "Anchor")!.Value;

        anchorText.Should().Be("1,0,4,0,2,0,5,0",
            "an untouched control's VML ClientData Anchor must round-trip unchanged");
    }

    private static MemoryStream BuildPackageWithCheckBoxControlAndVml()
    {
        var workbook = new Workbook("T");
        var sheet = workbook.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 9), new BoolValue(false)); // I4 linked cell

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

            // VML shape geometry matches the pre-shift controlPr anchor exactly: col1,off0,row4,off0
            // (from) .. col2,off0,row5,off0 (to), i.e. "1,0,4,0,2,0,5,0".
            var vml =
                "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" " +
                "xmlns:x=\"urn:schemas-microsoft-com:office:excel\">" +
                "<v:shape id=\"_x0000_s1025\" type=\"#_x0000_t201\" " +
                "style=\"position:absolute;margin-left:36pt;margin-top:57.75pt;width:73.5pt;height:15.75pt;z-index:1\" filled=\"f\">" +
                "<v:textbox><div style=\"text-align:left\">Check Box 1</div></v:textbox>" +
                "<x:ClientData ObjectType=\"Checkbox\">" +
                "<x:Anchor>1,0,4,0,2,0,5,0</x:Anchor><x:AutoFill>False</x:AutoFill><x:FmlaLink>$I$4</x:FmlaLink>" +
                "</x:ClientData></v:shape></xml>";
            ReplaceRawEntry(archive, "xl/drawings/vmlDrawing1.vml", vml);

            AddCtrlPropAndVmlRelationshipAndContentTypes(archive, worksheetPath);
        }

        result.Position = 0;
        return result;
    }

    private static void AddCtrlPropAndVmlRelationshipAndContentTypes(ZipArchive archive, string worksheetPath)
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
                new XAttribute("Id", "rIdCtrl"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp"),
                new XAttribute("Target", "../ctrlProps/ctrlProp1.xml")),
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
