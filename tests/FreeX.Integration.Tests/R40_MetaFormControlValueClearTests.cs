using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression coverage for R40-meta-1: r39 (see
/// <c>XlsxFormControlMixedCheckedPersistenceTests</c>) taught the WRITE side
/// (<see cref="XlsxWorksheetFormControlPreserver"/>) to prefer <see cref="FormControlModel.Value"/> == 2
/// ("Mixed") over <see cref="FormControlModel.IsChecked"/> when re-serializing a checkbox's
/// <c>checked</c> ctrlProp attribute on a full-rebuild save. But <see cref="FormControlInteractionService.CreateToggleCheckBoxCommand"/>
/// — the ONLY place a user click flips <see cref="FormControlModel.IsChecked"/> — never reset
/// <see cref="FormControlModel.Value"/> away from 2, so a Mixed control the user explicitly
/// checks/unchecks still had Value == 2 afterwards, and every subsequent save kept writing
/// <c>checked="Mixed"</c> to the XLSX forever, silently discarding the user's click.
///
/// <para>These tests assert that an explicit user toggle always commits a Mixed control to the
/// concrete 0/1 state matching the new <see cref="FormControlModel.IsChecked"/>, both in the
/// in-model state immediately after <c>Apply</c> and in the persisted ctrlProp XML after a
/// full-rebuild save — while an ordinary (already two-state) checkbox toggle is unaffected
/// (no regression).</para>
/// </summary>
public sealed class R40_MetaFormControlValueClearTests
{
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace FcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    // ── Primary: Mixed control toggled by the user must clear to a concrete state ──────────

    [Fact]
    public void ToggleCheckBox_MixedControl_ClearsValueToCheckedAndSavesChecked()
    {
        using var package = BuildPackageWithCheckBoxControl(checkedValue: "Mixed");
        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.Sheets[0];
        var control = sheet.FormControls.Single();

        // Sanity: loaded as Mixed (r38/r39 read-side behavior).
        control.Value.Should().Be(2, "the control was loaded from a checked=\"Mixed\" ctrlProp");
        control.IsChecked.Should().BeFalse("IsChecked cannot represent the third 'Mixed' state");

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, workbook);
        cmd.Should().NotBeNull("the control has a resolvable, writable linked cell");

        var ctx = new TestCommandContext(workbook);
        cmd!.Apply(ctx);

        control.IsChecked.Should().BeTrue("the user's click flips Unchecked-looking Mixed to Checked");
        control.Value.Should().Be(1,
            "R40-meta-1: an explicit user toggle must clear the inherited Mixed (2) reading to the " +
            "concrete Checked (1) state matching the new IsChecked");

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var ctrlPropXml = XDocument.Load(archive.GetEntry("xl/ctrlProps/ctrlProp1.xml")!.Open());
        ctrlPropXml.Root!.Attribute("checked")!.Value.Should().Be("Checked",
            "a control the user just explicitly checked must never be re-saved as \"Mixed\"");
    }

    [Fact]
    public void ToggleCheckBox_MixedControl_TwiceEndsUncheckedNotMixed()
    {
        using var package = BuildPackageWithCheckBoxControl(checkedValue: "Mixed");
        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.Sheets[0];
        var control = sheet.FormControls.Single();

        var ctx = new TestCommandContext(workbook);

        var first = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, workbook);
        first!.Apply(ctx);
        control.Value.Should().Be(1);

        var second = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, workbook);
        second!.Apply(ctx);

        control.IsChecked.Should().BeFalse("second click flips back off");
        control.Value.Should().Be(0,
            "a second explicit toggle must land on the concrete Unchecked (0) state, not resurrect Mixed");

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var ctrlPropXml = XDocument.Load(archive.GetEntry("xl/ctrlProps/ctrlProp1.xml")!.Open());
        ctrlPropXml.Root!.Attribute("checked")!.Value.Should().Be("Unchecked");
    }

    // ── Sibling no-regression: an ordinary (non-Mixed) toggle is unaffected ────────────────

    [Fact]
    public void ToggleCheckBox_OrdinaryUncheckedControl_TogglesToCheckedAsBefore()
    {
        using var package = BuildPackageWithCheckBoxControl(checkedValue: "Unchecked");
        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.Sheets[0];
        var control = sheet.FormControls.Single();

        control.Value.Should().Be(0);
        control.IsChecked.Should().BeFalse();

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, workbook);
        cmd.Should().NotBeNull();

        var ctx = new TestCommandContext(workbook);
        cmd!.Apply(ctx);

        control.IsChecked.Should().BeTrue();
        control.Value.Should().Be(1, "no regression: an ordinary two-state toggle still lands on Checked");

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var ctrlPropXml = XDocument.Load(archive.GetEntry("xl/ctrlProps/ctrlProp1.xml")!.Open());
        ctrlPropXml.Root!.Attribute("checked")!.Value.Should().Be("Checked");
    }

    [Fact]
    public void ToggleCheckBox_Undo_RestoresPriorMixedValue()
    {
        // Undo must restore the WHOLE prior state (including the tri-state Value), not just IsChecked
        // — otherwise undoing a toggle on a Mixed control would leave it stuck at the concrete state.
        using var package = BuildPackageWithCheckBoxControl(checkedValue: "Mixed");
        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.Sheets[0];
        var control = sheet.FormControls.Single();

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, workbook);
        var ctx = new TestCommandContext(workbook);
        cmd!.Apply(ctx);
        control.Value.Should().Be(1);

        cmd.Revert(ctx);

        control.IsChecked.Should().BeFalse("undo restores the prior IsChecked");
        control.Value.Should().Be(2, "undo restores the prior tri-state Mixed reading too");
    }

    // ── No-linked-cell case: an explicit toggle still clears Mixed even though nothing is written ──

    [Fact]
    public void ToggleCheckBox_MixedControlWithNoLinkedCell_StillClearsValue()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = false,
            Value = 2, // Mixed, but nothing to write to since there is no linked cell
            LinkedCell = null,
        };

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, workbook);

        cmd.Should().BeNull("no linked cell → no undoable command is produced");
        control.IsChecked.Should().BeTrue("the model flip still happens even without a linked cell");
        control.Value.Should().Be(1,
            "an explicit user toggle clears the inherited Mixed reading regardless of whether there is a linked cell to write");
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
