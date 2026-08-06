using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R123 (round 123, meta of r121): <see cref="XlsxWorksheetDrawingPartMerger.MergeAndGetDrawingPaths"/>
/// resolved the current worksheet PATH rename-safely via
/// <see cref="XlsxRenamedSourceSheetResolver.TryResolveTargetWorksheetPath"/>, but then looked the SHEET
/// MODEL up in the live <see cref="Workbook"/> using the same stale LOAD-TIME name
/// (<c>workbook?.GetSheet(sheetName)</c>) rather than the sheet's CURRENT (post-rename) name.
/// <see cref="Workbook.GetSheet(string)"/> matches only the sheet's current name, so after any in-session
/// rename the lookup returned <c>null</c>, <c>supersededSourceNames</c> became <c>null</c>, and
/// <c>MergeDrawingPart</c>'s supersession guard was skipped entirely -- defeating the r121
/// tombstone/supersede fix for every renamed sheet: a deleted source-loaded object's original anchor was
/// merged straight back in from the untouched source package, resurrecting it.
/// <para>
/// ROUND-TRIP FIXTURE RULE: every fixture here is built by saving a workbook with the REAL
/// <see cref="XlsxFileAdapter"/> writer and reloading it, so the object under test is genuinely
/// <c>IsSourceLoaded == true</c> before it is deleted -- never a hand-authored drawing XML fragment.
/// </para>
/// </summary>
public sealed class R123_RenameThenDeleteDrawingMergerTests
{
    [Fact]
    public void RenameSheetThenDeleteSourceLoadedPicture_SaveAndReload_DoesNotReappear()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("RenameThenDeletePicture");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var insert = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 2, 2), CreatePngBytes(), "image/png");
        insert.Apply(ctx).Success.Should().BeTrue();

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var picture = loadedSheet.Pictures.Should().ContainSingle().Which;
        picture.IsSourceLoaded.Should().BeTrue("a plain reloaded picture starts source-loaded");
        var loadedCtx = new TestCommandContext(loaded);

        // Rename the sheet FIRST -- this is the ordinary-UI order the defect requires: the rename
        // mutates sheet.Name in place (same Sheet/SheetId), so context.SourceSheets still keys this
        // sheet by its LOAD-TIME name ("Sheet1") while the live Workbook only knows it by "Renamed".
        var rename = new RenameSheetCommand(loadedSheet.Id, "Renamed");
        rename.Apply(loadedCtx).Success.Should().BeTrue();
        loadedSheet.Name.Should().Be("Renamed");

        var deleteCommand = new DeleteDrawingObjectCommand(loadedSheet.Id, SelectionPaneObjectKind.Picture, picture.Id);
        deleteCommand.Apply(loadedCtx).Success.Should().BeTrue();
        loadedSheet.Pictures.Should().BeEmpty("the delete command must remove the picture from the live model");

        using var deletedSave = new MemoryStream();
        adapter.Save(loaded, deletedSave);

        deletedSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(deletedSave);
        reloaded.GetSheet("Renamed")!.Pictures.Should().BeEmpty(
            "a deleted source-loaded picture must not be merged back in from the untouched source package " +
            "just because the sheet was renamed in the same session before the delete was saved");
    }

    [Fact]
    public void RenameSheetOnly_KeepsSourceLoadedPictureIntactOnSave()
    {
        // Neighbouring/no-regression sibling: a plain rename with NO delete must still merge the
        // untouched source drawing part forward normally (supersededSourceNames legitimately empty,
        // guard legitimately skipped) -- the picture must survive, not vanish.
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("RenameOnlyKeepsPicture");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var insert = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 2, 2), CreatePngBytes(), "image/png");
        insert.Apply(ctx).Success.Should().BeTrue();

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        loadedSheet.Pictures.Should().ContainSingle();
        var loadedCtx = new TestCommandContext(loaded);

        var rename = new RenameSheetCommand(loadedSheet.Id, "Renamed");
        rename.Apply(loadedCtx).Success.Should().BeTrue();

        using var renamedSave = new MemoryStream();
        adapter.Save(loaded, renamedSave);

        renamedSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(renamedSave);
        reloaded.GetSheet("Renamed")!.Pictures.Should().ContainSingle(
            "a plain sheet rename with no deletion must still preserve the sheet's untouched source picture");
    }

    private static byte[] CreatePngBytes()
    {
        // Minimal valid 1x1 transparent PNG.
        return
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];
    }
}
