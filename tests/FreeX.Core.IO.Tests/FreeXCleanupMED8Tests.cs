using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for FreeX cleanup batch MED8 (round-10 MED findings):
///  - P24: a cross-sheet linked picture (Paste Special &gt; Linked Picture pasted onto a sheet
///    other than its source range's sheet) must survive a native .fxl save/reload instead of being
///    silently dropped because it satisfied neither sheet's save filter.
///  - P26: a Paste-as-Picture cell-range snapshot's captured cell styling (fill/border/font/
///    alignment) must survive a native .fxl round trip instead of degrading to plain black
///    left-aligned text.
///  - P112: resurrecting a sheet-scoped defined name FreeX cannot model (e.g. an Excel-valid name
///    using characters FreeX's validator rejects) after a sheet delete/reorder must remap the
///    name's localSheetId onto the scope sheet's NEW index, not clone the stale pre-edit index.
/// </summary>
public sealed class FreeXCleanupMED8Tests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── P24 ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NativeJson_CrossSheetLinkedPicture_SurvivesSaveAndReload()
    {
        var workbook = new Workbook("Test");
        var sourceSheet = workbook.AddSheet("Sheet1");
        var targetSheet = workbook.AddSheet("Sheet2");

        // Simulate: copy Sheet1!A1:B2, switch to Sheet2, Paste Special > Linked Picture. The
        // picture is anchored on Sheet2 (where it visually sits) but its LinkedSourceRange points
        // back at Sheet1 (where the copied data lives) - a legitimate cross-sheet configuration
        // both WorkbookSession.PastePictureFromClipboardAtActiveCell and
        // MainWindow.ExecutePasteAsPicture allow.
        var sourceRange = new GridRange(
            new CellAddress(sourceSheet.Id, 1, 1),
            new CellAddress(sourceSheet.Id, 2, 2));
        var picture = new PictureModel
        {
            Anchor = new CellAddress(targetSheet.Id, 3, 3),
            Kind = PictureKind.CellRangeSnapshot,
            IsLinkedToSourceRange = true,
            LinkedSourceRange = sourceRange,
            LinkedSourceSheetName = sourceSheet.Name,
            SourceRowCount = 2,
            SourceColumnCount = 2
        };
        targetSheet.Pictures.Add(picture);

        using var saved = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new NativeJsonAdapter().Load(saved);
        var reloadedTargetSheet = reloaded.GetSheetAt(1);

        reloadedTargetSheet.Pictures.Should().ContainSingle(
            "the cross-sheet linked picture must not be silently dropped from every sheet's saved list");
        var reloadedPicture = reloadedTargetSheet.Pictures[0];
        reloadedPicture.Anchor.Sheet.Should().Be(reloadedTargetSheet.Id);
        reloadedPicture.IsLinkedToSourceRange.Should().BeTrue();

        var reloadedSourceSheet = reloaded.GetSheetAt(0);
        reloadedPicture.LinkedSourceRange.Should().NotBeNull();
        reloadedPicture.LinkedSourceRange!.Value.Start.Sheet.Should().Be(
            reloadedSourceSheet.Id,
            "the linked source range must still point at the SOURCE sheet, not the anchor sheet, after reload");
        reloadedPicture.LinkedSourceRange.Value.ToString().Should().Be("A1:B2");
    }

    // ── P26 ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NativeJson_PictureCellSnapshot_PreservesStyleAndNumericFlag_OnRoundTrip()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var capturedStyle = new CellStyle
        {
            Bold = true,
            FontColor = CellColor.FromArgb(255, 0, 0),
            FillColor = CellColor.FromArgb(255, 255, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.CellRangeSnapshot,
            SourceRowCount = 1,
            SourceColumnCount = 1
        };
        picture.Cells.Add(new PictureCellSnapshot(0, 0, "123", capturedStyle, IsNumericOrDate: true));
        sheet.Pictures.Add(picture);

        using var saved = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new NativeJsonAdapter().Load(saved);
        var reloadedPicture = reloaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        var reloadedCell = reloadedPicture.Cells.Should().ContainSingle().Subject;

        reloadedCell.Text.Should().Be("123");
        reloadedCell.IsNumericOrDate.Should().BeTrue(
            "the numeric/date flag drives the WPF/Avalonia renderers' alignment and must not be lost");
        reloadedCell.Style.Should().NotBeNull(
            "the captured cell style (fills/borders/font/alignment) must not be dropped on reload");
        reloadedCell.Style!.Bold.Should().BeTrue();
        reloadedCell.Style.FontColor.Should().Be(CellColor.FromArgb(255, 0, 0));
        reloadedCell.Style.FillColor.Should().Be(CellColor.FromArgb(255, 255, 0));
        reloadedCell.Style.HorizontalAlignment.Should().Be(HorizontalAlignment.Right);
    }

    // ── P112 ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_AfterSheetDelete_ResurrectedUnmodelableSheetScopedName_RemapsToNewSheetIndex()
    {
        // Three-sheet source package with a sheet-scoped defined name FreeX cannot model (Excel
        // permits backslash in a defined name; FreeX's stricter validator rejects it), scoped via
        // localSheetId="2" to the THIRD sheet ("Keep").
        var sourceBytes = CreateThreeSheetSourcePackageWithUnmodelableName(
            name: "Dept\\East",
            scopedToSheetLocalId: 2,
            refersToBody: "Keep!$A$1");

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        // Confirm the premise: this name is invisible to the model (never loaded), matching P110's
        // established never-loaded-in-the-first-place reasoning that P112 builds on.
        workbook.NamedRanges.Should().NotContainKey("Dept\\East");
        workbook.NamedFormulas.Should().NotContainKey("Dept\\East");

        // Delete the FIRST sheet ("Drop"). This shifts "Keep" from original index 2 down to index 1,
        // and the sheet-count change forces the full-save path (RestoreWorkbookDefinedNames against
        // the pristine pre-edit snapshot).
        var dropSheet = workbook.Sheets.Single(s => s.Name == "Drop");
        workbook.RemoveSheet(dropSheet.Id).Should().BeTrue();
        workbook.Sheets.Should().HaveCount(2);
        var keepSheetNewIndex = workbook.Sheets.ToList().FindIndex(s => s.Name == "Keep");
        keepSheetNewIndex.Should().Be(1);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        var definedNames = ReadDefinedNameElements(saved.ToArray());
        var resurrected = definedNames.Where(e => e.Attribute("name")?.Value == "Dept\\East").ToList();

        resurrected.Should().ContainSingle(
            "the name must be resurrected exactly once, not duplicated under both the old and new index");
        var localSheetId = resurrected[0].Attribute("localSheetId")?.Value;
        localSheetId.Should().Be(
            "1",
            "localSheetId must be remapped to Keep's NEW index (1) after Drop's deletion shifted it down from 2, " +
            "not left cloned at the stale pre-delete index 2 (which would now scope the name to a different " +
            "sheet, or be out of range if it were the last sheet)");
    }

    private static List<XElement> ReadDefinedNameElements(byte[] packageBytes)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("xl/workbook.xml");
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        return document.Root!
            .Element(WorkbookNs + "definedNames")
            ?.Elements(WorkbookNs + "definedName")
            .ToList()
            ?? [];
    }

    /// <summary>
    /// Builds a 3-sheet ("Drop", "Middle", "Keep") source package and injects a sheet-scoped
    /// defined name at the given ORIGINAL localSheetId, with a name FreeX's validator rejects (so
    /// it round-trips only via the unconditional-resurrection path, never through the live model).
    /// </summary>
    private static byte[] CreateThreeSheetSourcePackageWithUnmodelableName(
        string name,
        int scopedToSheetLocalId,
        string refersToBody)
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            workbook.AddWorksheet("Drop").Cell("A1").Value = "drop sheet data";
            workbook.AddWorksheet("Middle").Cell("A1").Value = "middle sheet data";
            workbook.AddWorksheet("Keep").Cell("A1").Value = "keep sheet data";
            workbook.SaveAs(stream);
        }

        var bytes = stream.ToArray();
        using var editStream = new MemoryStream();
        editStream.Write(bytes, 0, bytes.Length);
        using (var archive = new ZipArchive(editStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument workbookXml;
            using (var entryStream = entry.Open())
                workbookXml = XDocument.Load(entryStream);

            var root = workbookXml.Root!;

            // Remove any empty <definedNames/> ClosedXML may have emitted, matching the other
            // cleanup-batch fixtures' convention, then add our sheet-scoped unmodelable name.
            foreach (var existing in root.Elements(WorkbookNs + "definedNames").ToList())
                existing.Remove();

            var definedNames = new XElement(WorkbookNs + "definedNames");
            root.Element(WorkbookNs + "sheets")!.AddAfterSelf(definedNames);
            definedNames.Add(new XElement(
                WorkbookNs + "definedName",
                new XAttribute("name", name),
                new XAttribute("localSheetId", scopedToSheetLocalId),
                refersToBody));

            entry.Delete();
            var replacement = archive.CreateEntry("xl/workbook.xml");
            using var replacementStream = replacement.Open();
            workbookXml.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        return editStream.ToArray();
    }
}
