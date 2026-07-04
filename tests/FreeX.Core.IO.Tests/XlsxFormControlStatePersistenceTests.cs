using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for the I-form-controls review group, finding H16: a form control's live
/// interaction state (IsChecked / Value / SelectedIndex) must be written back into the preserved
/// <c>ctrlProp</c> part on save, so reloading the saved file reflects the user's interaction instead
/// of silently reverting to the control's file-load state.
///
/// Builds a valid FreeX package, grafts a worksheet <c>controls</c> block + <c>ctrlProp</c> part
/// onto it (the same technique <see cref="XlsxFormControlLoadRoundTripTests"/> uses), loads it,
/// mutates the loaded <see cref="FormControlModel"/> to simulate a user interaction, saves, and
/// asserts the re-saved <c>ctrlProp</c> XML reflects the new state.
/// </summary>
public sealed class XlsxFormControlStatePersistenceTests
{
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace FcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    [Fact]
    public void SaveAfterToggling_CheckBox_WritesCheckedStateToCtrlProp()
    {
        using var package = BuildPackageWithCheckBoxControl(initiallyChecked: false, linkedCell: "$I$4");
        var workbook = new XlsxFileAdapter().Load(package);

        var sheet = workbook.Sheets[0];
        var control = sheet.FormControls.Single();
        control.IsChecked.Should().BeFalse("sanity: loaded state matches the source ctrlProp");

        // Simulate the user clicking the checkbox (in-model + linked-cell write), without going
        // through the WPF/Avalonia click handler.
        control.IsChecked = true;
        sheet.SetCell(new CellAddress(sheet.Id, 4, 9), Cell.FromValue(new BoolValue(true)));

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var ctrlPropEntry = archive.GetEntry("xl/ctrlProps/ctrlProp1.xml");
        ctrlPropEntry.Should().NotBeNull();

        var ctrlPropXml = XDocument.Load(ctrlPropEntry!.Open());
        ctrlPropXml.Root!.Attribute("checked")!.Value.Should().Be("Checked",
            "the ctrlProp must reflect the control's CURRENT IsChecked, not its file-load state");
    }

    [Fact]
    public void SaveAfterToggling_CheckBox_ReloadedControlIsChecked()
    {
        // End-to-end: save then reload — the checkbox must come back checked, matching the linked
        // cell (which already round-trips correctly via the ordinary cell-value path).
        using var package = BuildPackageWithCheckBoxControl(initiallyChecked: false, linkedCell: "$I$4");
        var workbook = new XlsxFileAdapter().Load(package);

        var sheet = workbook.Sheets[0];
        var control = sheet.FormControls.Single();
        control.IsChecked = true;
        sheet.SetCell(new CellAddress(sheet.Id, 4, 9), Cell.FromValue(new BoolValue(true)));

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSheet = reloaded.Sheets[0];
        reloadedSheet.FormControls.Should().ContainSingle();
        reloadedSheet.FormControls[0].IsChecked.Should().BeTrue(
            "reloading must show the control's state as it was when saved, not its original file-load state");

        reloadedSheet.GetCell(new CellAddress(reloadedSheet.Id, 4, 9))!.Value.Should().Be(new BoolValue(true));
    }

    [Fact]
    public void SaveWithoutInteraction_CheckBox_StillUncheckedAfterReload()
    {
        // Negative control: an untouched control's ctrlProp must remain byte-identical (Unchecked).
        using var package = BuildPackageWithCheckBoxControl(initiallyChecked: false, linkedCell: "$I$4");
        var workbook = new XlsxFileAdapter().Load(package);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.Sheets[0].FormControls.Single().IsChecked.Should().BeFalse(
            "an untouched control must round-trip its original state unchanged");
    }

    private static MemoryStream BuildPackageWithCheckBoxControl(bool initiallyChecked, string linkedCell)
    {
        var workbook = new Workbook("T");
        var sheet = workbook.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 9), Cell.FromValue(new BoolValue(initiallyChecked))); // I4 linked cell

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

            var ctrlPropXml = new XDocument(new XElement(FcNs + "formControlPr",
                new XAttribute("objectType", "CheckBox"),
                new XAttribute("checked", initiallyChecked ? "Checked" : "Unchecked"),
                new XAttribute("lockText", "1"),
                new XAttribute("noThreeD", "1"),
                new XAttribute("fmlaLink", linkedCell)));
            ReplaceEntry(archive, "xl/ctrlProps/ctrlProp1.xml", ctrlPropXml);

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
                    new XAttribute("Target", "../ctrlProps/ctrlProp1.xml")));
            ReplaceEntry(archive, relsPath, relsXml);

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
}
