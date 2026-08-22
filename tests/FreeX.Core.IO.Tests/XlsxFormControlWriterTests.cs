using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxFormControlWriterTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace FormControlNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";

    [Fact]
    public void Save_NewLegacyControls_WritesPackageGraphAndRoundTripsModeledState()
    {
        var workbook = new Workbook("Controls");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.FormControls.Add(new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            Caption = "Enable totals",
            Anchor = Anchor(sheet, 2, 2, 3, 4),
            AnchorOffsets = new DrawingAnchorRange(
                new DrawingAnchorPoint(1, 9525, 1, 19050),
                new DrawingAnchorPoint(3, 28575, 2, 38100)),
            LinkedCell = "$H$2",
            IsChecked = true,
        });
        sheet.FormControls.Add(new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            Caption = "Quantity",
            Anchor = Anchor(sheet, 5, 2, 6, 2),
            Value = 4,
            Min = 1,
            Max = 10,
            Increment = 2,
        });
        sheet.FormControls.Add(new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            Caption = "Category",
            Anchor = Anchor(sheet, 8, 2, 9, 3),
            LinkedCell = "$J$2",
            ListFillRange = "$A$1:$A$3",
            SelectedIndex = 2,
        });

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        using (var archive = OpenRead(saved))
        {
            var worksheetPath = GetWorksheetPath(archive);
            var worksheetXml = LoadXml(archive, worksheetPath);
            var controls = worksheetXml.Descendants(WorksheetNs + "control").ToList();
            controls.Should().HaveCount(3);
            controls.All(control => control.Attribute(RelNs + "id") != null).Should().BeTrue();
            worksheetXml.Root!.Element(WorksheetNs + "legacyDrawing").Should().NotBeNull();

            var ctrlPropEntries = archive.Entries.Where(entry => entry.FullName.StartsWith("xl/ctrlProps/", StringComparison.OrdinalIgnoreCase)).ToList();
            ctrlPropEntries.Should().HaveCount(3);
            ctrlPropEntries.Select(entry => LoadXml(archive, entry.FullName).Root!.Name).Should().OnlyContain(name => name == FormControlNs + "formControlPr");
            var vmlEntry = archive.Entries.Single(entry => entry.FullName.EndsWith(".vml", StringComparison.OrdinalIgnoreCase));
            LoadXml(archive, vmlEntry.FullName).Descendants(VmlNs + "shape").Should().HaveCount(3);

            var relationshipXml = LoadXml(archive, XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
            var controlRelationships = relationshipXml.Descendants(PackageRelNs + "Relationship")
                .Where(relationship => string.Equals(relationship.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp", StringComparison.OrdinalIgnoreCase))
                .ToList();
            controlRelationships.Should().HaveCount(3);
            controlRelationships.All(relationship => archive.GetEntry(XlsxPackagePath.ResolveRelationshipTarget(
                worksheetPath,
                relationship.Attribute("Target")!.Value)) is not null).Should().BeTrue();

            var vmlRelationship = relationshipXml.Descendants(PackageRelNs + "Relationship").Single(relationship =>
                string.Equals(relationship.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing", StringComparison.OrdinalIgnoreCase));
            archive.GetEntry(XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, vmlRelationship.Attribute("Target")!.Value)).Should().NotBeNull();
        }

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var controlsAfterReload = reloaded.Sheets.Single().FormControls;
        controlsAfterReload.Should().HaveCount(3);

        var checkBox = controlsAfterReload.Single(control => control.Kind == FormControlKind.CheckBox);
        checkBox.Caption.Should().Be("Enable totals");
        checkBox.IsChecked.Should().BeTrue();
        checkBox.LinkedCell.Should().Be("$H$2");
        checkBox.Anchor.Should().Be(Anchor(reloaded.Sheets.Single(), 2, 2, 3, 4));
        checkBox.AnchorOffsets.Should().Be(new DrawingAnchorRange(
            new DrawingAnchorPoint(1, 9525, 1, 19050),
            new DrawingAnchorPoint(3, 28575, 2, 38100)));

        var spinner = controlsAfterReload.Single(control => control.Kind == FormControlKind.Spinner);
        spinner.Value.Should().Be(4);
        spinner.Min.Should().Be(1);
        spinner.Max.Should().Be(10);
        spinner.Increment.Should().Be(2);

        var dropDown = controlsAfterReload.Single(control => control.Kind == FormControlKind.DropDown);
        dropDown.SelectedIndex.Should().Be(2);
        dropDown.ListFillRange.Should().Be("$A$1:$A$3");
    }

    [Fact]
    public void Save_LoadedWorkbookWithNewLegacyControl_KeepsExistingControlAndAuthorsNewPackageParts()
    {
        var original = new Workbook("Controls");
        var originalSheet = original.AddSheet("Sheet1");
        originalSheet.FormControls.Add(new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            Caption = "Existing",
            Anchor = Anchor(originalSheet, 1, 1, 2, 2),
            IsChecked = true,
        });

        using var firstSave = new MemoryStream();
        new XlsxFileAdapter().Save(original, firstSave);
        firstSave.Position = 0;
        var loaded = new XlsxFileAdapter().Load(firstSave);
        var loadedSheet = loaded.Sheets.Single();
        loadedSheet.FormControls.Add(new FormControlModel
        {
            Kind = FormControlKind.ScrollBar,
            Caption = "New",
            Anchor = Anchor(loadedSheet, 4, 1, 5, 3),
            Value = 5,
            Min = 0,
            Max = 20,
            PageChange = 3,
        });

        using var secondSave = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, secondSave);

        secondSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(secondSave);
        reloaded.Sheets.Single().FormControls.Should().HaveCount(2);
        reloaded.Sheets.Single().FormControls.Single(control => control.Caption == "Existing").IsChecked.Should().BeTrue();
        var scrollBar = reloaded.Sheets.Single().FormControls.Single(control => control.Kind == FormControlKind.ScrollBar);
        scrollBar.Value.Should().Be(5);
        scrollBar.Max.Should().Be(20);
        scrollBar.PageChange.Should().Be(3);
    }

    [Fact]
    public void Save_AllSupportedLegacyControlKinds_RoundTripAsTheirOriginalKinds()
    {
        var workbook = new Workbook("All controls");
        var sheet = workbook.AddSheet("Sheet1");
        var supportedKinds = new[]
        {
            FormControlKind.Button,
            FormControlKind.CheckBox,
            FormControlKind.OptionButton,
            FormControlKind.DropDown,
            FormControlKind.ListBox,
            FormControlKind.GroupBox,
            FormControlKind.Label,
            FormControlKind.ScrollBar,
            FormControlKind.Spinner,
        };

        for (var index = 0; index < supportedKinds.Length; index++)
        {
            sheet.FormControls.Add(new FormControlModel
            {
                Kind = supportedKinds[index],
                Caption = supportedKinds[index].ToString(),
                Anchor = Anchor(sheet, (uint)(index + 1), 1, (uint)(index + 1), 2),
            });
        }

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.Sheets.Single().FormControls.Select(control => control.Kind).Should().BeEquivalentTo(supportedKinds);
    }

    [Fact]
    public void Save_ControlsInsertedThroughCommand_RoundTripAsSupportedLegacyControls()
    {
        var workbook = new Workbook("Inserted controls");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        var kinds = new[]
        {
            FormControlKind.CheckBox,
            FormControlKind.OptionButton,
            FormControlKind.Button,
            FormControlKind.DropDown,
            FormControlKind.ListBox,
            FormControlKind.Spinner,
            FormControlKind.ScrollBar,
        };

        for (var index = 0; index < kinds.Length; index++)
        {
            new AddFormControlCommand(sheet.Id, new CellAddress(sheet.Id, (uint)(index + 1), 2), kinds[index])
                .Apply(context).Success.Should().BeTrue();
        }

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        var controls = reloaded.Sheets.Single().FormControls;
        controls.Select(control => control.Kind).Should().BeEquivalentTo(kinds);
        controls.All(control => control.Anchor is not null && control.ShapeId is not null).Should().BeTrue();
        controls.Single(control => control.Kind == FormControlKind.Spinner).Max.Should().Be(100);
        controls.Single(control => control.Kind == FormControlKind.ScrollBar).PageChange.Should().Be(10);
    }

    private static GridRange Anchor(Sheet sheet, uint startRow, uint startColumn, uint endRow, uint endColumn) =>
        new(new CellAddress(sheet.Id, startRow, startColumn), new CellAddress(sheet.Id, endRow, endColumn));

    private static ZipArchive OpenRead(MemoryStream stream)
    {
        stream.Position = 0;
        return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
    }

    private static string GetWorksheetPath(ZipArchive archive)
    {
        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var workbookRels = XlsxRelationshipReader.LoadTargets(
            archive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            PackageRelNs);
        return XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(workbookXml, workbookRels, WorksheetNs, RelNs).Single().WorksheetPath;
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        return XDocument.Load(stream);
    }
}
