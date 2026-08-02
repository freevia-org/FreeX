using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R118-io-duplicate-sheet-form-control-1: a legacy Form Control (CheckBox/OptionButton/ScrollBar/
/// SpinButton/ListBox/DropDown/Button) IS faithfully cloned in memory onto a duplicated sheet by
/// <c>DuplicateSheetDrawingCloner.CopyDrawingCollections</c> (same ShapeId, remapped Anchor), but the
/// package-level &lt;controls&gt;/&lt;legacyDrawing&gt;/ctrlProps triad that actually makes the control
/// visible/interactive in Excel was previously written ONLY by
/// <see cref="XlsxWorksheetFormControlPreserver.Preserve"/>, whose per-sheet loop iterates exclusively
/// over sheets present in the ORIGINALLY LOADED package -- a sheet created via Duplicate Sheet never
/// had an on-disk counterpart at load time, so the control silently vanished from the saved package on
/// reload even though the in-memory model still carried it. Mirrors
/// <c>R77_DuplicateSheetQueryTableTests</c>, the equivalent fix already shipped for legacy queryTables.
/// </summary>
public sealed class R118_DuplicateSheetFormControlTests
{
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace FcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private const string ControlPropertiesRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp";

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void DuplicateSheet_WithCheckBoxFormControl_SurvivesSaveReloadOnTheCopy()
    {
        using var source = BuildPackageWithCheckBoxControlOnNamedSheet("Source", linkedCell: "$I$4");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedSource = loaded.GetSheet("Source")!;
        loadedSource.FormControls.Should().ContainSingle("sanity: the source sheet's control loads");

        var ctx = new TestCommandContext(loaded);
        new DuplicateSheetCommand(loadedSource.Id).Apply(ctx).Success.Should().BeTrue();
        loaded.Sheets.Select(s => s.Name).Should().Contain("Source (2)");

        var copySheet = loaded.GetSheet("Source (2)")!;
        copySheet.FormControls.Should().ContainSingle(
            "sanity: DuplicateSheetDrawingCloner already clones the in-memory FormControlModel");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.GetSheet("Source (2)")!.FormControls.Should().ContainSingle(
            "the duplicated sheet's checkbox must round-trip through save+reload, not silently vanish");
        reloaded.GetSheet("Source")!.FormControls.Should().ContainSingle(
            "the ORIGINAL sheet's control must be completely unaffected by duplicating its sheet");
    }

    [Fact]
    public void DuplicateSheet_WithCheckBoxFormControl_ClonesDistinctCtrlPropPart_NotSharedWithOriginal()
    {
        using var source = BuildPackageWithCheckBoxControlOnNamedSheet("Source", linkedCell: "$I$4");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedSource = loaded.GetSheet("Source")!;
        var ctx = new TestCommandContext(loaded);
        new DuplicateSheetCommand(loadedSource.Id).Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var originalWorksheetPath = GetWorksheetPathForSheetName(savedArchive, "Source");
        var copyWorksheetPath = GetWorksheetPathForSheetName(savedArchive, "Source (2)");

        var originalCtrlPropPath = GetCtrlPropTargetForWorksheet(savedArchive, originalWorksheetPath);
        var copyCtrlPropPath = GetCtrlPropTargetForWorksheet(savedArchive, copyWorksheetPath);

        originalCtrlPropPath.Should().NotBeNullOrEmpty("the original sheet's own ctrlProp relationship must be untouched");
        copyCtrlPropPath.Should().NotBeNullOrEmpty("the duplicated sheet must gain its own ctrlProp relationship");
        copyCtrlPropPath.Should().NotBe(
            originalCtrlPropPath,
            "the duplicate must get its own ctrlProp part, matching real Excel's Duplicate Sheet " +
            "behavior, not a second relationship aimed at the original's part (sharing one part would " +
            "let an edit to either sheet's control corrupt the other's saved state)");

        savedArchive.GetEntry(copyCtrlPropPath!).Should().NotBeNull(
            "the cloned relationship must point at a real ctrlProp part in the saved package");
    }

    // Sibling no-regression case: duplicating a plain sheet with no form control at all must still
    // save cleanly with no spurious <controls>/ctrlProp introduced.
    [Fact]
    public void DuplicateSheet_PlainSheetWithNoFormControl_SavesCleanly_WithNoControlsIntroduced()
    {
        var workbook = new Workbook("PlainDuplicate");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var ctx = new TestCommandContext(loaded);

        new DuplicateSheetCommand(loadedSheet.Id).Apply(ctx).Success.Should().BeTrue();
        loaded.Sheets.Select(s => s.Name).Should().Contain("Sheet1 (2)");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        savedArchive.Entries.Should().NotContain(
            entry => entry.FullName.StartsWith("xl/ctrlProps/", StringComparison.OrdinalIgnoreCase),
            "duplicating a plain sheet with no form control must never fabricate one");

        var copyWorksheetPath = GetWorksheetPathForSheetName(savedArchive, "Sheet1 (2)");
        var xml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(copyWorksheetPath)!);
        xml.Root!.Elements(WorksheetNs + "controls").Should().BeEmpty();
    }

    private static string? GetCtrlPropTargetForWorksheet(ZipArchive archive, string worksheetPath)
    {
        var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is null)
            return null;

        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        var relationship = relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .SingleOrDefault(r => r.Attribute("Type")?.Value == ControlPropertiesRelationshipType);
        if (relationship is null)
            return null;

        return XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, relationship.Attribute("Target")!.Value);
    }

    private static string GetWorksheetPathForSheetName(ZipArchive archive, string sheetName)
    {
        var workbookXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("xl/workbook.xml")!);
        var workbookRels = XlsxRelationshipReader.LoadTargets(archive, "xl/_rels/workbook.xml.rels", "xl/workbook.xml", PackageRelNs);
        return XlsxWorkbookSheetPathReader
            .GetWorkbookSheetPaths(workbookXml, workbookRels, WorksheetNs, RelNs)
            .Single(pair => pair.SheetName == sheetName)
            .WorksheetPath;
    }

    /// <summary>
    /// Builds a workbook with a single sheet (<paramref name="sheetName"/>, carrying some cell content
    /// plus a legacy CheckBox Form Control), saves it via FreeX's own writer, then hand-injects the
    /// &lt;controls&gt;/ctrlProp/relationship triad FreeX has no writer for at all -- mirrors
    /// XlsxFormControlShiftPersistenceTests.BuildPackageWithCheckBoxControl, the established pattern
    /// for testing this preserver. A hand-authored fixture is unavoidable here (not a violation of the
    /// round-trip-fixture rule): FreeX can only PRESERVE an existing legacy Form Control loaded from a
    /// source package -- it has no writer that can author the &lt;controls&gt;/ctrlProp parts fresh --
    /// so there is no FreeX-writer round trip this fixture could be built from instead. The worksheet
    /// skeleton and cell content ARE produced by FreeX's own writer/reader; only the form-control-
    /// specific parts are hand-injected, exactly like every other test in this file's sibling suite.
    /// </summary>
    private static MemoryStream BuildPackageWithCheckBoxControlOnNamedSheet(string sheetName, string linkedCell)
    {
        var workbook = new Workbook("T");
        var sheet = workbook.AddSheet(sheetName);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 9), new BoolValue(false)); // I4 linked cell

        using var baseStream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, baseStream);
        baseStream.Position = 0;

        var result = new MemoryStream();
        baseStream.CopyTo(result);
        result.Position = 0;

        using (var archive = new ZipArchive(result, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetPath = GetWorksheetPathForSheetName(archive, sheetName);
            var worksheetEntry = archive.GetEntry(worksheetPath)!;

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

    private static void AddCtrlPropRelationshipAndContentTypes(ZipArchive archive, string worksheetPath)
    {
        var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
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
                new XAttribute("Type", ControlPropertiesRelationshipType),
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
