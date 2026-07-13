using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R39-meta-2: r38 fixed only the READ side of Excel's tri-state
/// ("Mixed") checkbox/option-button encoding (<see cref="XlsxFormControlMapper.ReadControlProperties"/>
/// populates <see cref="FormControlModel.Value"/> == 2 for a "Mixed" <c>checked</c> attribute), but
/// the WRITE side used by the full-rebuild save path
/// (<see cref="XlsxWorksheetFormControlPreserver"/>'s <c>ApplyControlStateToFormControlPr</c>,
/// exercised via <c>WriteControlStateToCtrlProps</c>) still wrote only
/// <c>checked ? "Checked" : "Unchecked"</c> from <see cref="FormControlModel.IsChecked"/> (which is
/// always <see langword="false"/> for a Mixed control), silently downgrading a Mixed control to
/// Unchecked on the very next save. These tests assert a Mixed control's ctrlProp round-trips as
/// "Mixed" through a save-without-editing (the full-rebuild path is exercised by every
/// <see cref="XlsxFileAdapter"/> save/reload), while plain Checked/Unchecked controls are unaffected
/// (no regression to the already-working two-state case).
/// </summary>
public sealed class XlsxFormControlMixedCheckedPersistenceTests
{
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace FcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    [Fact]
    public void SaveAfterLoad_MixedCheckBox_WritesMixedToCtrlProp()
    {
        using var package = BuildPackageWithCheckBoxControl(checkedValue: "Mixed");
        var workbook = new XlsxFileAdapter().Load(package);
        var control = workbook.Sheets[0].FormControls.Single();

        // Sanity: the read side (fixed by r38) must have captured the tri-state "Mixed" reading.
        control.Value.Should().Be(2, "Value carries Excel's tri-state ST_Checked encoding for CheckBox/OptionButton");
        control.IsChecked.Should().BeFalse("IsChecked cannot represent the third 'Mixed' state");

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var ctrlPropXml = XDocument.Load(archive.GetEntry("xl/ctrlProps/ctrlProp1.xml")!.Open());
        ctrlPropXml.Root!.Attribute("checked")!.Value.Should().Be("Mixed",
            "a Mixed control must not be downgraded to Unchecked on a full-rebuild save");
    }

    [Fact]
    public void SaveAfterLoad_MixedCheckBox_ReloadedControlStillReportsMixed()
    {
        using var package = BuildPackageWithCheckBoxControl(checkedValue: "Mixed");
        var workbook = new XlsxFileAdapter().Load(package);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedControl = reloaded.Sheets[0].FormControls.Single();
        reloadedControl.Value.Should().Be(2,
            "reloading after the round trip must still show the tri-state Mixed reading, not a silently downgraded Unchecked");
        reloadedControl.IsChecked.Should().BeFalse();
    }

    [Theory]
    [InlineData("Checked", 1, true)]
    [InlineData("Unchecked", 0, false)]
    public void SaveAfterLoad_TwoStateCheckBox_RoundTripsUnchanged(string checkedValue, int expectedValue, bool expectedIsChecked)
    {
        // Negative control: the ordinary two-state Checked/Unchecked case must still round-trip
        // correctly (no regression from the Mixed fix).
        using var package = BuildPackageWithCheckBoxControl(checkedValue);
        var workbook = new XlsxFileAdapter().Load(package);
        var control = workbook.Sheets[0].FormControls.Single();
        control.Value.Should().Be(expectedValue);
        control.IsChecked.Should().Be(expectedIsChecked);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var ctrlPropXml = XDocument.Load(archive.GetEntry("xl/ctrlProps/ctrlProp1.xml")!.Open());
            ctrlPropXml.Root!.Attribute("checked")!.Value.Should().Be(checkedValue,
                "an untouched two-state control's checked attribute must round-trip unchanged");
        }

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedControl = reloaded.Sheets[0].FormControls.Single();
        reloadedControl.Value.Should().Be(expectedValue);
        reloadedControl.IsChecked.Should().Be(expectedIsChecked);
    }

    private static MemoryStream BuildPackageWithCheckBoxControl(string checkedValue)
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
                new XAttribute("checked", checkedValue),
                new XAttribute("lockText", "1"),
                new XAttribute("noThreeD", "1"),
                new XAttribute("fmlaLink", "$I$4")));
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
