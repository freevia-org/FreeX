using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for the incremental XLSX patch-save path's handling of the user's current
/// scroll position (sheetView@topLeftCell / Sheet.ViewTopRow+ViewLeftCol).
///
/// XlsxWorksheetViewBaseline (the record used by XlsxWorksheetViewPatch.TryCreate to detect which
/// per-sheet view attributes changed since the last save) used to omit ViewTopRow/ViewLeftCol from
/// its tracked fields, and ApplyWorksheetViewChanges re-seeded the synthetic Sheet it hands to the
/// writer from whatever topLeftCell was already on disk (ReadExistingTopLeftCell) instead of the
/// live model's current scroll position.
///
/// Investigation finding: an UNRELATED safety net already protects against silent data loss here.
/// XlsxCellPatchBaseline.TryGetPatchableValueChanges validates that reverting every tracked patch
/// category reproduces the ORIGINAL patch-validation fingerprint (CreatePatchValidationModelFingerprint),
/// and that fingerprint's per-sheet DTO unconditionally includes ViewTopRow/ViewLeftCol (see
/// NativeJsonAdapter.Save.cs). Since the untracked scroll-position change was never reverted, that
/// validation always failed and the save was escalated to "change_unsupported_model_delta" ->
/// XlsxSavePath.FullSave -- which correctly writes the live ViewTopRow/ViewLeftCol via
/// XlsxWorksheetViewWriter.Save. So no scroll position was ever actually LOST on disk; every
/// affected save was just (silently, wastefully) demoted from the fast patch path to a full
/// ClosedXML rebuild -- defeating the entire point of patch-save for exactly the large-workbook
/// case it exists to speed up. These tests pin the real, provable regression: an edit that should
/// be patch-save-eligible must actually take the patch path (LastSaveDiagnostics.Path ==
/// XlsxSavePath.SourcePatch), not be silently escalated to a full save, while the persisted
/// ViewTopRow/ViewLeftCol value is correct either way.
///
/// These tests drive the real product entry point (XlsxFileAdapter.Load -&gt;
/// TryPrepareLoadedPackageSnapshotForEdit -&gt; mutate -&gt; Save -&gt; reload), matching the pattern used
/// by the sibling R28_ViewSplitPaneRoundTripTests.
/// </summary>
public sealed class R107_PatchSaveScrollPositionPersistenceTests
{
    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    private static byte[] CreateSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "original value";
            sheet.Cell("B2").Value = 123.45;
            workbook.SaveAs(stream);
        }

        return stream.ToArray();
    }

    private static Workbook LoadAndPrepareForEdit(byte[] sourceBytes, XlsxFileAdapter adapter)
    {
        using var source = new MemoryStream(sourceBytes, writable: false);
        var workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);
        return workbook;
    }

    [Fact]
    public void ScrollOnlyChange_NoOtherEdit_UsesPatchSaveAndPersistsTopLeftCell()
    {
        // The user opens the file, scrolls down to row 200 (nothing else changes at all -- no
        // cell edit, no active-cell move), and hits Ctrl+S. This is the common "just scrolled,
        // saved" workflow the finding describes. Before the fix, XlsxWorksheetViewBaseline didn't
        // track the scroll position at all, so TryGetPatchableValueChanges' internal fingerprint
        // consistency check always detected an "unsupported model delta" and silently escalated
        // to a full ClosedXML rebuild on every such save (a real perf regression that defeats the
        // purpose of patch-save for the large-workbook case it exists for) instead of using the
        // fast patch path.
        var adapter = new XlsxFileAdapter();
        var workbook = LoadAndPrepareForEdit(CreateSourcePackage(), adapter);
        var sheet = workbook.GetSheetAt(0);

        sheet.ViewTopRow = 200u;
        sheet.ViewLeftCol = 3u;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.SourcePatch,
            "a scroll-only change is fully representable by the patch path and must not force an " +
            "unnecessary full-rebuild save");

        var reloadedAdapter = new XlsxFileAdapter();
        using var reloadStream = new MemoryStream(saved.ToArray(), writable: false);
        var reloaded = reloadedAdapter.Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.ViewTopRow.Should().Be(200u, "the user's current scroll position must survive the save");
        reloadedSheet.ViewLeftCol.Should().Be(3u);
    }

    [Fact]
    public void ScrollChangeTogetherWithActiveCellMove_UsesPatchSaveAndPersistsBoth()
    {
        // The "Worse:" scenario from the finding: the user ALSO moves the active cell (a tracked
        // field), so a view patch IS created for that reason. Before the fix,
        // ApplyWorksheetViewChanges re-seeded ViewTopRow/ViewLeftCol from the stale on-disk value
        // instead of the live model's current scroll position; the fingerprint-consistency check
        // (comparing against the ORIGINAL patch-validation fingerprint after reverting the tracked
        // fields) then detected the untracked scroll delta and escalated to a full save on every
        // such combined edit too.
        var adapter = new XlsxFileAdapter();
        var workbook = LoadAndPrepareForEdit(CreateSourcePackage(), adapter);
        var sheet = workbook.GetSheetAt(0);

        sheet.ViewTopRow = 50u;
        sheet.ViewLeftCol = 2u;
        sheet.ActiveRow = 60u;
        sheet.ActiveCol = 2u;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.SourcePatch,
            "an active-cell move plus a scroll change are both fully representable by the patch " +
            "path and must not force an unnecessary full-rebuild save");

        var reloadedAdapter = new XlsxFileAdapter();
        using var reloadStream = new MemoryStream(saved.ToArray(), writable: false);
        var reloaded = reloadedAdapter.Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.ActiveRow.Should().Be(60u, "the active-cell move was already tracked and must keep working");
        reloadedSheet.ActiveCol.Should().Be(2u);
        reloadedSheet.ViewTopRow.Should().Be(
            50u,
            "the scroll position set alongside the active-cell move must not be clobbered back to the stale on-disk value");
        reloadedSheet.ViewLeftCol.Should().Be(2u);
    }

    [Fact]
    public void ActiveCellMoveOnly_SiblingCase_StillUsesPatchSaveAndLeavesTopLeftCellUnset()
    {
        // Sibling no-regression case: when only the active cell moves (scroll position genuinely
        // unchanged from what was already on disk -- both start out unset/null here), this already
        // used the patch path before the fix and must keep doing so, without inventing or
        // otherwise disturbing a topLeftCell/scroll position that was never touched.
        var adapter = new XlsxFileAdapter();
        var workbook = LoadAndPrepareForEdit(CreateSourcePackage(), adapter);
        var sheet = workbook.GetSheetAt(0);

        sheet.ViewTopRow.Should().BeNull("freshly loaded source file has no explicit topLeftCell");
        sheet.ViewLeftCol.Should().BeNull();

        sheet.ActiveRow = 9u;
        sheet.ActiveCol = 4u;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);

        var reloadedAdapter = new XlsxFileAdapter();
        using var reloadStream = new MemoryStream(saved.ToArray(), writable: false);
        var reloaded = reloadedAdapter.Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.ActiveRow.Should().Be(9u);
        reloadedSheet.ActiveCol.Should().Be(4u);
        reloadedSheet.ViewTopRow.Should().BeNull("no scroll position was ever set, so none should appear");
        reloadedSheet.ViewLeftCol.Should().BeNull();
    }
}
