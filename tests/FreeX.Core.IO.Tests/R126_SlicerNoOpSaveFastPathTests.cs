using System.IO;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round-126 finding Core.IO-1 (MED): a large workbook (over
/// XlsxSourcePackage's FingerprintCellLimit of 100,000 populated cells) that also carries a
/// patch-unsafe feature -- a slicer or timeline being the common case, since
/// WorkbookHasPatchUnsafePivotFeatures blocks patch-save unconditionally whenever any are present
/// -- permanently lost BOTH no-op-save fast paths:
/// <list type="bullet">
/// <item>Fast path 1 (XlsxSourcePackage.Matches, in Save.cs) never captures a whole-model
///   fingerprint above the cell limit (ShouldCaptureModelFingerprint returns false), so it can
///   never prove "nothing changed" for a large workbook on its own.</item>
/// <item>Fast path 2 (TrySavePatchedCellValues's zero-real-change CopyTo) was gated behind
///   TryEnsureCellPatchEligibility, which fails unconditionally whenever a slicer/timeline is
///   present -- regardless of whether it (or anything else) actually changed -- so the zero-diff
///   check was never even reached.</item>
/// </list>
/// Together this forced EVERY save of such a workbook through the full ClosedXML rebuild, which
/// unconditionally bumps docProps/core.xml's dcterms:modified and cp:revision
/// (XlsxDocumentPropertiesPreserver.UpdateModifiedAndRevisionOnSave) even when the user made zero
/// edits between saves -- e.g. opening the file and immediately pressing Ctrl+S -- guaranteeing two
/// saves of a literally unchanged large+slicer workbook differ byte-for-byte.
///
/// The fix adds XlsxSourcePackage.NonCellModelFingerprint -- a cheap, cell-count-INDEPENDENT
/// fingerprint (CreatePatchValidationModelFingerprint, which excludes cell values but still covers
/// slicers/timelines/pivot caches/custom views/charts/pictures/text boxes/drawing shapes) captured
/// synchronously alongside Buffer at every Capture/Rebase point -- and a new last-chance check,
/// TryCopyUnchangedPatchUnsafeWorkbook, that runs when TryEnsureCellPatchEligibility rejects a
/// workbook for a patch-unsafe feature. It proves the stronger claim ("the feature -- and
/// everything else outside per-cell values -- is unchanged since the source snapshot AND the
/// ordinary 6-category cell diff is empty too") before allowing the raw-byte CopyTo shortcut, so a
/// genuine no-op save still takes it, while a real change (to a cell OR to the patch-unsafe
/// feature itself) still falls through to the unmodified full-rebuild fallback.
///
/// Both tests LOAD the authored bytes via XlsxFileAdapter.Load before exercising Save -- the real
/// product entry point that establishes XlsxSourcePackage tracking (a workbook that is merely
/// constructed via the model API and saved from scratch never gets a source-package snapshot
/// captured at all -- see ApplyPackagePostProcessing's `if (!hasSourcePackage) { ...; return; }`
/// early-out -- so a from-scratch Save-then-Save-again can never reach either fast path and would
/// not exercise this fix).
/// </summary>
public sealed class R126_SlicerNoOpSaveFastPathTests
{
    private const int RowsWithCells = 1010;
    private const int ColsPerRow = 100; // 1010 * 100 = 101,000 > FingerprintCellLimit (100,000)

    [Fact]
    public void LargeWorkbookWithSlicer_UnchangedResaveAfterLoad_TakesSourceCopyFastPath_ByteIdentical()
    {
        var authoredBytes = AuthorLargeWorkbookWithSlicerBytes();

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(authoredBytes, writable: false))
            workbook = adapter.Load(source);

        workbook.GetSheetAt(0).CellCount.Should().BeGreaterThan(100_000,
            "the fixture must exceed XlsxSourcePackage.FingerprintCellLimit so fast path 1 (the " +
            "whole-model fingerprint) is disabled and this test actually exercises the " +
            "cell-count-independent fast path added by this fix");
        workbook.Slicers.Should().ContainSingle();

        // Save #1 post-load: zero edits since Load -- exactly "open the file and immediately press
        // Ctrl+S". Before the fix: XlsxSourcePackage.Matches can't prove "unchanged" above the cell
        // limit (ModelFingerprint is null), and TryEnsureCellPatchEligibility fails unconditionally
        // because a slicer is present, so this was ALWAYS a full ClosedXML rebuild -- which
        // unconditionally re-stamps dcterms:modified and bumps cp:revision even though nothing
        // changed.
        using var firstSave = new MemoryStream();
        adapter.Save(workbook, firstSave);
        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.SourceCopy,
            adapter.LastSaveDiagnostics.Reason);
        firstSave.ToArray().Should().Equal(
            authoredBytes,
            "an unedited save right after load must replay the exact bytes that were loaded, not " +
            "re-run the full rebuild and bump docProps/core.xml's modified timestamp/revision for " +
            "a genuine no-op save");

        // Save #2: still zero edits -- "saving it twice in a row" from the finding's concrete
        // scenario. Must be just as much a no-op as save #1.
        using var secondSave = new MemoryStream();
        adapter.Save(workbook, secondSave);
        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.SourceCopy,
            adapter.LastSaveDiagnostics.Reason);
        secondSave.ToArray().Should().Equal(firstSave.ToArray(),
            "two consecutive no-edit saves of the same large+slicer workbook must be byte-identical");
    }

    /// <summary>
    /// Sibling/no-regression coverage for the SHARED-FEATURE concern this fix has to get right: a
    /// change that touches ONLY the patch-unsafe feature itself (here, the slicer's layout), with
    /// none of the six ordinary diff categories (cell values, dimensions, merges, hyperlinks,
    /// comments, worksheet views) touched, must NOT be silently dropped by the new no-op
    /// short-circuit. TryCopyUnchangedPatchUnsafeWorkbook's own PatchUnsafeFeatureModelFingerprint
    /// comparison is exactly what must catch this -- if it didn't, the CopyTo shortcut would replay
    /// the stale pre-edit bytes and the user's slicer change would vanish on save. (Whether the
    /// full-rebuild WRITER faithfully encodes this particular slicer attribute for an
    /// already-loaded workbook is a separate, pre-existing concern this test does not assert on --
    /// see this round's siblingLeads.)
    /// </summary>
    [Fact]
    public void WorkbookWithSlicer_SlicerOnlyChangeAfterLoad_StillTakesFullRebuild_NotTheNoOpShortcut()
    {
        var authoredBytes = AuthorSmallWorkbookWithSlicerBytes();

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(authoredBytes, writable: false))
            workbook = adapter.Load(source);
        var slicer = workbook.Slicers.Should().ContainSingle().Subject;

        // No cell/dimension/merge/hyperlink/comment/view edit at all -- only the slicer's own
        // layout changes. If the new no-op short-circuit only checked what it checked before this
        // fix (nothing, since eligibility already blocked patch-save unconditionally) or were keyed
        // on the six cell-level diff categories alone, this would be wrongly treated as a no-op and
        // the change would be silently dropped. SlicerModel's properties are init-only, so the
        // "edit" is modeled as the command layer would do it: replace the model instance in place
        // with the changed shape.
        workbook.Slicers[0] = new SlicerModel
        {
            Name = slicer.Name,
            CacheName = slicer.CacheName,
            Caption = slicer.Caption,
            SourcePivotTableName = slicer.SourcePivotTableName,
            SourceFieldName = slicer.SourceFieldName,
            StyleName = slicer.StyleName,
            ColumnCount = 4,
            ShowCaption = false
        };

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        // Must NOT take the new no-op shortcut -- the slicer genuinely changed since the source
        // snapshot, so PatchUnsafeFeatureModelFingerprint must NOT match and this must fall through
        // to the (unmodified) full ClosedXML rebuild fallback exactly as it did before this fix.
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().NotBe("model_unchanged_patch_unsafe_feature_present");
        saved.ToArray().Should().NotEqual(
            authoredBytes,
            "a real slicer-layout change must not be silently dropped by the no-op fast path");
    }

    private static byte[] AuthorLargeWorkbookWithSlicerBytes()
    {
        var workbook = new Workbook("LargeSlicerNoOpSave");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 1; row <= RowsWithCells; row++)
        {
            for (uint col = 1; col <= ColsPerRow; col++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * 1000 + col));
            }
        }

        sheet.CellCount.Should().BeGreaterThan(100_000);
        workbook.Slicers.Add(NewSlicer());

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    private static byte[] AuthorSmallWorkbookWithSlicerBytes()
    {
        var workbook = new Workbook("SlicerOnlyChangeNoRegression");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        workbook.Slicers.Add(NewSlicer());

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    private static SlicerModel NewSlicer() => new()
    {
        Name = "Region Slicer",
        CacheName = "Slicer_Region",
        Caption = "Region",
        SourcePivotTableName = "PivotTable1",
        SourceFieldName = "Region",
        StyleName = "SlicerStyleLight2",
        ColumnCount = 1,
        ShowCaption = true
    };
}
