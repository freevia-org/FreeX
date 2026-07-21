using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round-57 findings R57-io-protection-5-1 and R57-io-protection-5-2,
/// fixed in XlsxFileAdapter.SourcePackageSnapshot.cs
/// (WorksheetProtectionStateChanged/TryReadSheetProtectionPackageGuardInfo).
///
/// Both findings share the same root cause: the cell-patch eligibility guard
/// (PackageAllowsCellPatchSave -&gt; WorksheetProtectionStateChanged) only compared
/// Sheet.IsProtected + the password hash against the source bytes. A Protect-Sheet
/// PERMISSION-flag edit (R57-io-protection-5-1) or an Allow-Edit-Range add/remove
/// (R57-io-protection-5-2), with no other cell/dimension/merge/hyperlink/comment/view
/// change, therefore took the "model_unchanged_after_patch_baseline" CopyTo(stream)
/// shortcut and silently discarded the change -- patch-save's NormalizePatchWorksheetProtection
/// only cosmetically normalizes the *original* sheetProtection/protectedRanges elements, it
/// never re-derives either from the model.
/// </summary>
public sealed class R57_IoProtectionPatchSaveEligibilityTests
{
    // ---- R57-io-protection-5-1: sheet-protection permission-flag edit ------------------------

    [Fact]
    public void Save_TogglingSheetProtectionPermissionOnlyOnProtectedSheet_ForcesFullSaveAndPersists()
    {
        var workbook = new Workbook("SheetProtectionPermissionRegressionTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));
        sheet.IsProtected = true;
        // Sheet.ProtectionPermissions defaults to {SelectLockedCells, SelectUnlockedCells} only --
        // every other permission (including FormatCells) starts denied.

        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.IsProtected.Should().BeTrue();
        loadedSheet.ProtectionPermissions.Should().NotContain(SheetProtectionPermission.FormatCells);

        // Grant FormatCells -- the ONLY change since load. No cell/dimension/merge/hyperlink/
        // comment/view edit accompanies it, so this exercises exactly the eligibility-guard gap:
        // WorksheetProtectionStateChanged used to compare only IsProtected + password hash, never
        // ProtectionPermissions, so this edit alone would previously fall all the way through to
        // the zero-changes CopyTo(stream) shortcut and vanish.
        loadedSheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatCells);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.FullSave,
            "a permission-flag-only edit must fail cell-patch eligibility so the full save path " +
            "re-derives <sheetProtection> from the model instead of silently keeping the source bytes");
        adapter.LastSaveDiagnostics.Reason.Should().Be("worksheet_postprocessing_protection_changed");

        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.IsProtected.Should().BeTrue();
        reloadedSheet.ProtectionPermissions.Should().Contain(
            SheetProtectionPermission.FormatCells,
            "the granted permission must persist through save/reload instead of being silently dropped");
    }

    [Fact]
    public void Save_PlainCellEditOnProtectedSheetWithNoPermissionChange_StillTakesCellPatchPath()
    {
        // Sibling no-regression case: an ordinary cell-only edit on an already-protected sheet
        // (no protection-state delta of any kind) must still take the cheap cell-patch path --
        // the new permission/allow-edit-range comparison must not over-trigger a full save for
        // edits that never touch protection at all.
        var workbook = new Workbook("SheetProtectionUnchangedRegressionTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));
        sheet.IsProtected = true;

        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 4, 1), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.SourcePatch,
            "an edit that never touches protection state must still be eligible for the cheap " +
            "cell-patch path");
    }

    // ---- R57-io-protection-5-2: Allow-Edit-Range add ------------------------------------------

    [Fact]
    public void Save_AddingAllowEditRangeOnProtectedSheet_ForcesFullSaveAndPersists()
    {
        var workbook = new Workbook("AllowEditRangeAddRegressionTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));
        sheet.IsProtected = true;

        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.AllowEditRanges.Should().BeEmpty();

        // Add an Allow-Edit-Range entry -- the ONLY change since load. Just like the permission
        // case above, this alone used to fall through to the zero-changes CopyTo(stream) shortcut
        // because WorksheetProtectionStateChanged never compared AllowEditRanges/
        // AllowEditRangePasswords against the source protectedRanges.
        var range = new GridRange(
            new CellAddress(loadedSheet.Id, 2, 2),
            new CellAddress(loadedSheet.Id, 4, 4));
        loadedSheet.AllowEditRanges.Add(range);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.FullSave,
            "an Allow-Edit-Range addition must fail cell-patch eligibility so the full save path " +
            "re-derives <protectedRanges> from the model instead of silently keeping the source bytes");
        adapter.LastSaveDiagnostics.Reason.Should().Be("worksheet_postprocessing_protection_changed");

        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.AllowEditRanges.Should().ContainSingle().Which.Should().Be(range);
    }

    [Fact]
    public void Save_PlainCellEditOnSheetWithUnchangedAllowEditRange_StillTakesCellPatchPath()
    {
        // Sibling no-regression case: a sheet that already has an Allow-Edit-Range entry (carried
        // over unchanged from the source bytes) must still take the cheap cell-patch path for an
        // ordinary cell-only edit -- the new comparison must not treat an unchanged range as a
        // false-positive protection-state delta.
        var workbook = new Workbook("AllowEditRangeUnchangedRegressionTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));
        sheet.IsProtected = true;
        sheet.AllowEditRanges.Add(new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 4, 4)));

        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.AllowEditRanges.Should().ContainSingle();
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 6, 1), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.SourcePatch,
            "an edit that leaves the existing Allow-Edit-Range entry untouched must still be " +
            "eligible for the cheap cell-patch path");
    }
}
