using System.Reflection;
using System.Collections;
using System.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R127-viewstate-delete-purge-2: <see cref="WorkbookSession"/> keeps a tenth SheetId-keyed
/// per-view cache, <c>_viewViewportOrigins</c> (this view's own remembered scroll TopRow/LeftCol
/// per sheet, exactly mirroring the per-window-independence pattern used by the nine override
/// dictionaries and <c>_splitPaneViewportOffsets</c>), that r126
/// (<see cref="R126_DeleteActiveSheetPurgesPerViewOverrideCachesTests"/>) missed: its
/// <c>InvalidateAllPerViewOverridesForSheet</c> choke point never covered
/// <c>_viewViewportOrigins</c>, and <see cref="WorkbookSession.DeleteActiveSheet"/> only ever
/// called <c>_splitPaneViewportOffsets.Remove(sheetId)</c> alongside it -- so every deleted sheet
/// permanently left a stale scroll-origin entry behind for the rest of the session's lifetime.
/// Fixed by also calling <c>_viewViewportOrigins.Remove(sheetId)</c> inside
/// <see cref="WorkbookSession.DeleteActiveSheet"/>.
/// </summary>
public sealed class R127_DeleteActiveSheetPurgesViewportOriginCacheTests
{
    /// <summary>
    /// Seeds this view's own scroll origin for the sheet about to be deleted (through the real
    /// public <see cref="WorkbookSession.SetViewportOrigin"/> entry point the View/scrollbar
    /// commands use), deletes that sheet, and asserts the SheetId-keyed
    /// <c>_viewViewportOrigins</c> cache no longer contains the deleted sheet's id.
    /// </summary>
    [Fact]
    public void DeleteActiveSheet_PurgesViewportOriginCacheForDeletedSheet()
    {
        var workbook = CreateWorkbook();
        var root = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var details = workbook.AddSheet("Details");
        root.SelectSheet(details.Id).Should().BeTrue();
        var sibling = root.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);
        sibling.ActiveSheet.Should().BeSameAs(details);

        // InitializeSiblingView already seeds an entry at construction time, but drive the real
        // public scroll entry point too so the seeded value is unambiguously this test's own,
        // matching how a real View pan/scroll would leave the cache.
        sibling.SetViewportOrigin(40, 6).Should().BeTrue();
        sibling.ViewportOrigin.Should().Be((40u, 6u));

        GetDictionaryKeys(sibling, "_viewViewportOrigins").Should().Contain(
            details.Id,
            "_viewViewportOrigins should be seeded before delete, otherwise this test would trivially pass either way");

        var result = sibling.DeleteActiveSheet();

        result.Success.Should().BeTrue();
        workbook.Sheets.Should().NotContain(s => s.Id == details.Id);

        GetDictionaryKeys(sibling, "_viewViewportOrigins").Should().NotContain(
            details.Id,
            "the viewport-origin cache must drop the deleted sheet's entry, not leak it for the rest of the session");
    }

    /// <summary>
    /// No-regression sibling: deleting one sheet must not disturb another *surviving* sheet's own
    /// remembered scroll origin -- the purge must be scoped to the deleted sheet id only, and after
    /// the post-delete active-sheet switch the surviving sheet's own remembered origin (not the
    /// deleted sheet's, and not the workbook default) must still be what is read back.
    /// </summary>
    [Fact]
    public void DeleteActiveSheet_LeavesSurvivingSheetsOwnViewportOriginIntact()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.SetViewportOrigin(12, 3).Should().BeTrue();
        // Keep the active cell pinned to the scrolled-to top-left corner so the later switch back
        // to Summary (after Details is deleted) does NOT legitimately re-clamp the origin via
        // EnsureActiveCellVisible -- that auto-scroll-to-keep-selection-visible behavior is
        // correct product behavior, not the regression this test targets; anchoring the active
        // cell inside the scrolled viewport isolates the assertion to the purge itself.
        session.SelectCell(new CellAddress(summary.Id, 12, 3));

        session.SelectSheet(details.Id).Should().BeTrue();
        session.SetViewportOrigin(99, 20).Should().BeTrue();
        session.SelectCell(new CellAddress(details.Id, 99, 20));

        var result = session.DeleteActiveSheet();

        result.Success.Should().BeTrue();
        workbook.Sheets.Should().ContainSingle().Which.Id.Should().Be(summary.Id);

        session.ViewportOrigin.Should().Be((12u, 3u));
        GetDictionaryKeys(session, "_viewViewportOrigins").Should().NotContain(details.Id);
    }

    private static List<SheetId> GetDictionaryKeys(WorkbookSession session, string fieldName)
    {
        var field = typeof(WorkbookSession).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(WorkbookSession), fieldName);
        var dict = field.GetValue(session)
            ?? throw new InvalidOperationException($"{fieldName} was null.");
        var keysProperty = dict.GetType().GetProperty("Keys")
            ?? throw new InvalidOperationException($"{fieldName} has no Keys property.");
        var keys = (IEnumerable)keysProperty.GetValue(dict)!;
        return keys.Cast<SheetId>().ToList();
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
