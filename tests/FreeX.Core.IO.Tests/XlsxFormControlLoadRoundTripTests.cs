using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// End-to-end coverage for legacy form-control load (parse into the model) and round-trip
/// preservation. Builds a valid FreeX package, injects a worksheet <c>controls</c> block plus a
/// <c>ctrlProp</c> part + relationships into the zip (the way Excel stores form controls), then
/// loads it and re-saves it.
/// </summary>
public sealed class XlsxFormControlLoadRoundTripTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void Load_WorksheetWithCheckBoxControl_ParsesFormControlIntoModel()
    {
        using var package = BuildPackageWithCheckBoxControl();

        var workbook = new XlsxFileAdapter().Load(package);

        var sheet = workbook.Sheets[0];
        sheet.FormControls.Should().ContainSingle();
        var control = sheet.FormControls[0];
        control.Kind.Should().Be(FormControlKind.CheckBox);
        control.IsChecked.Should().BeTrue();
        control.LinkedCell.Should().Be("$I$4");
    }

    [Fact]
    public void SaveAfterLoad_WorksheetWithCheckBoxControl_PreservesControlsAndCtrlProps()
    {
        using var package = BuildPackageWithCheckBoxControl();
        var workbook = new XlsxFileAdapter().Load(package);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        // The ctrlProp part must survive.
        archive.GetEntry("xl/ctrlProps/ctrlProp1.xml").Should().NotBeNull("the control properties part must round-trip");

        // The worksheet must still reference the control (a bare orphan ctrlProp is invisible in Excel).
        var sheetEntry = archive.Entries.Single(e =>
            e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        var sheetXml = XDocument.Load(sheetEntry.Open());
        sheetXml.Descendants(WorksheetNs + "control").Should().NotBeEmpty(
            "the worksheet <control> reference must round-trip so Excel re-attaches the control");
    }

    private static MemoryStream BuildPackageWithCheckBoxControl()
    {
        // Start from a valid FreeX-saved package, then graft the form-control parts onto it.
        var workbook = new Workbook("T");
        var sheet = workbook.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 9), new BoolValue(true)); // I4 linked cell

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

            // Inject the controls block + legacyDrawing into the worksheet XML.
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
                                <from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></from>
                                <to><xdr:col>3</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>3</xdr:row><xdr:rowOff>0</xdr:rowOff></to>
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

            // ctrlProp part.
            XNamespace fcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
            var ctrlPropXml = new XDocument(new XElement(fcNs + "formControlPr",
                new XAttribute("objectType", "CheckBox"),
                new XAttribute("checked", "Checked"),
                new XAttribute("lockText", "1"),
                new XAttribute("noThreeD", "1"),
                new XAttribute("fmlaLink", "$I$4")));
            ReplaceEntry(archive, "xl/ctrlProps/ctrlProp1.xml", ctrlPropXml);

            // A minimal VML drawing for the legacyDrawing relationship.
            var vml =
                "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\">" +
                "<v:shape id=\"CheckBox1\" type=\"#_x0000_t201\"><x:ClientData ObjectType=\"Checkbox\">" +
                "<x:Anchor>1,0,1,0,3,0,3,0</x:Anchor><x:Checked>1</x:Checked><x:FmlaLink>$I$4</x:FmlaLink>" +
                "</x:ClientData></v:shape></xml>";
            ReplaceRawEntry(archive, "xl/drawings/vmlDrawing1.vml", vml);

            // Worksheet relationships: ctrlProp + vmlDrawing.
            var relsPath = "xl/worksheets/_rels/" + System.IO.Path.GetFileName(worksheetPath) + ".rels";
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
                    new XAttribute("Id", "rIdVml"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing"),
                    new XAttribute("Target", "../drawings/vmlDrawing1.vml")),
                new XElement(PackageRelNs + "Relationship",
                    new XAttribute("Id", "rIdCtrl"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp"),
                    new XAttribute("Target", "../ctrlProps/ctrlProp1.xml")));
            ReplaceEntry(archive, relsPath, relsXml);

            // Content types for ctrlProps + vml.
            EnsureContentTypes(archive);
        }

        result.Position = 0;
        return result;
    }

    private static void EnsureContentTypes(ZipArchive archive)
    {
        XNamespace ctNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var entry = archive.GetEntry("[Content_Types].xml")!;
        XDocument xml;
        using (var read = entry.Open())
            xml = XDocument.Load(read);
        var root = xml.Root!;
        if (!root.Elements(ctNs + "Default").Any(d => string.Equals(d.Attribute("Extension")?.Value, "vml", StringComparison.OrdinalIgnoreCase)))
        {
            root.Add(new XElement(ctNs + "Default",
                new XAttribute("Extension", "vml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.vmlDrawing")));
        }
        root.Add(new XElement(ctNs + "Override",
            new XAttribute("PartName", "/xl/ctrlProps/ctrlProp1.xml"),
            new XAttribute("ContentType", "application/vnd.ms-excel.controlproperties+xml")));
        ReplaceEntry(archive, "[Content_Types].xml", xml);
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
