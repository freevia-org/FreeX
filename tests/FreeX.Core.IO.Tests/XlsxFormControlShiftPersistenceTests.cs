using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R26-form-controls-deep-1/2: a structural edit (row/column insert-delete)
/// shifts a form control's LinkedCell/ListFillRange/Anchor in memory (see
/// <c>RowColumnShiftHelpers.AddressState.ShiftFormControls</c>), but
/// <see cref="XlsxWorksheetFormControlPreserver"/>'s save path previously never wrote those shifted
/// values back into the package — a reload silently re-linked the control to its stale, pre-edit
/// cell and re-drew it at its stale, pre-edit position. These tests simulate the post-shift
/// <see cref="FormControlModel"/> state directly (mutating the loaded model the same way
/// <c>ShiftFormControls</c> would, without depending on FreeX.Core.Commands) and assert the
/// save-then-reload round trip reflects the shift, while an UNTOUCHED control still round-trips
/// unchanged (no regression to the already-working case).
/// </summary>
public sealed class XlsxFormControlShiftPersistenceTests
{
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace FcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    [Fact]
    public void SaveAfterShift_CheckBox_WritesShiftedLinkedCellToCtrlProp()
    {
        using var package = BuildPackageWithCheckBoxControl(linkedCell: "$I$4");
        var workbook = new XlsxFileAdapter().Load(package);
        var control = workbook.Sheets[0].FormControls.Single();
        control.LinkedCell.Should().Be("$I$4", "sanity: loaded state matches the source ctrlProp");

        // Simulate RowColumnShiftHelpers.AddressState.ShiftFormControls rewriting LinkedCell after a
        // row insert above the control (e.g. "$I$4" -> "$I$5"), without depending on Core.Commands.
        control.LinkedCell = "$I$5";

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var ctrlPropXml = XDocument.Load(archive.GetEntry("xl/ctrlProps/ctrlProp1.xml")!.Open());
        ctrlPropXml.Root!.Attribute("fmlaLink")!.Value.Should().Be("$I$5",
            "the ctrlProp must reflect the shifted LinkedCell, not the stale pre-shift reference");
    }

    [Fact]
    public void SaveAfterShift_CheckBox_ReloadedControlHasShiftedLinkedCell()
    {
        using var package = BuildPackageWithCheckBoxControl(linkedCell: "$I$4");
        var workbook = new XlsxFileAdapter().Load(package);
        var control = workbook.Sheets[0].FormControls.Single();
        control.LinkedCell = "$I$5";

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.Sheets[0].FormControls.Single().LinkedCell.Should().Be("$I$5",
            "reloading must show the shifted linked cell, not silently re-link to the stale $I$4");
    }

    [Fact]
    public void SaveWithoutShift_CheckBox_LinkedCellRoundTripsUnchanged()
    {
        // Negative control: an untouched control's LinkedCell must still round-trip (no regression
        // to the ordinary save-without-editing path).
        using var package = BuildPackageWithCheckBoxControl(linkedCell: "$I$4");
        var workbook = new XlsxFileAdapter().Load(package);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.Sheets[0].FormControls.Single().LinkedCell.Should().Be("$I$4",
            "an untouched control's linked cell must round-trip unchanged");
    }

    [Fact]
    public void SaveAfterShift_ListBox_WritesShiftedListFillRangeToCtrlProp()
    {
        using var package = BuildPackageWithListBoxControl(listFillRange: "$E$1:$E$3");
        var workbook = new XlsxFileAdapter().Load(package);
        var control = workbook.Sheets[0].FormControls.Single();
        control.ListFillRange.Should().Be("$E$1:$E$3", "sanity: loaded state matches the source ctrlProp");

        // Simulate ShiftFormControls rewriting ListFillRange after a row insert above the list's
        // source range (e.g. "$E$1:$E$3" -> "$E$2:$E$4").
        control.ListFillRange = "$E$2:$E$4";

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.Sheets[0].FormControls.Single().ListFillRange.Should().Be("$E$2:$E$4",
            "reloading must show the shifted list fill range, not the stale pre-shift range");
    }

    [Fact]
    public void SaveWithoutShift_ListBox_ListFillRangeRoundTripsUnchanged()
    {
        using var package = BuildPackageWithListBoxControl(listFillRange: "$E$1:$E$3");
        var workbook = new XlsxFileAdapter().Load(package);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.Sheets[0].FormControls.Single().ListFillRange.Should().Be("$E$1:$E$3",
            "an untouched control's list fill range must round-trip unchanged");
    }

    [Fact]
    public void SaveAfterShift_CheckBox_ReloadedControlHasShiftedAnchor()
    {
        using var package = BuildPackageWithCheckBoxControl(linkedCell: "$I$4");
        var workbook = new XlsxFileAdapter().Load(package);
        var control = workbook.Sheets[0].FormControls.Single();

        var anchor = control.Anchor!.Value;
        anchor.Start.Row.Should().Be(5, "sanity: source anchor is B5:C6 (1-based)");
        anchor.Start.Col.Should().Be(2);
        anchor.End.Row.Should().Be(6);
        anchor.End.Col.Should().Be(3);

        // Simulate ShiftFormControls rewriting Anchor/AnchorOffsets after a row insert above the
        // control (B5:C6 -> B6:C7), without depending on Core.Commands.
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

        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedControl = reloaded.Sheets[0].FormControls.Single();
        var reloadedAnchor = reloadedControl.Anchor!.Value;
        reloadedAnchor.Start.Row.Should().Be(6,
            "reloading must show the shifted anchor (B6), not the stale pre-shift position (B5)");
        reloadedAnchor.Start.Col.Should().Be(2);
        reloadedAnchor.End.Row.Should().Be(7);
        reloadedAnchor.End.Col.Should().Be(3);
    }

    [Fact]
    public void SaveWithoutShift_CheckBox_AnchorRoundTripsUnchanged()
    {
        // Negative control: an untouched control's anchor must still round-trip (no regression to
        // the ordinary save-without-editing path).
        using var package = BuildPackageWithCheckBoxControl(linkedCell: "$I$4");
        var workbook = new XlsxFileAdapter().Load(package);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedAnchor = reloaded.Sheets[0].FormControls.Single().Anchor!.Value;
        reloadedAnchor.Start.Row.Should().Be(5, "an untouched control's anchor must round-trip unchanged");
        reloadedAnchor.Start.Col.Should().Be(2);
        reloadedAnchor.End.Row.Should().Be(6);
        reloadedAnchor.End.Col.Should().Be(3);
    }

    private static MemoryStream BuildPackageWithCheckBoxControl(string linkedCell)
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
                new XAttribute("fmlaLink", linkedCell)));
            ReplaceEntry(archive, "xl/ctrlProps/ctrlProp1.xml", ctrlPropXml);

            AddCtrlPropRelationshipAndContentTypes(archive, worksheetPath);
        }

        result.Position = 0;
        return result;
    }

    private static MemoryStream BuildPackageWithListBoxControl(string listFillRange)
    {
        var workbook = new Workbook("T");
        workbook.AddSheet("S");

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
                          <control shapeId="1026" r:id="rIdCtrl" name="List Box 1">
                            <controlPr defaultSize="0" autoFill="0" autoLine="0" autoPict="0">
                              <anchor>
                                <from><xdr:col>4</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></from>
                                <to><xdr:col>5</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>3</xdr:row><xdr:rowOff>0</xdr:rowOff></to>
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
                new XAttribute("objectType", "List"),
                new XAttribute("fmlaRange", listFillRange),
                new XAttribute("sel", "0")));
            ReplaceEntry(archive, "xl/ctrlProps/ctrlProp1.xml", ctrlPropXml);

            AddCtrlPropRelationshipAndContentTypes(archive, worksheetPath);
        }

        result.Position = 0;
        return result;
    }

    private static void AddCtrlPropRelationshipAndContentTypes(ZipArchive archive, string worksheetPath)
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
                new XAttribute("Target", "../ctrlProps/ctrlProp1.xml")));
        ReplaceEntry(archive, relsPath, relsXml);

        XNamespace ctNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var ctEntry = archive.GetEntry("[Content_Types].xml")!;
        XDocument ctXml;
        using (var read = ctEntry.Open())
            ctXml = XDocument.Load(read);
        ctXml.Root!.Add(new XElement(ctNs + "Override",
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
}
