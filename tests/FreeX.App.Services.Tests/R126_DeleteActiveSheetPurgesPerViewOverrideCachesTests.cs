using System.Reflection;
using System.Collections;
using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R126-viewstate-delete-purge-1: <see cref="WorkbookSession"/> keeps nine SheetId-keyed
/// per-view override dictionaries (<c>_viewZoomOverrides</c>, <c>_viewModeOverrides</c>,
/// <c>_viewShowGridlinesOverrides</c>, <c>_viewShowHeadingsOverrides</c>,
/// <c>_viewShowFormulasOverrides</c>, <c>_viewFrozenRowsOverrides</c>,
/// <c>_viewFrozenColsOverrides</c>, <c>_viewSplitRowOverrides</c>, <c>_viewSplitColOverrides</c>)
/// plus <c>_splitPaneViewportOffsets</c>, each holding this view's own remembered per-sheet view
/// state independent of every other window/view over the same workbook.
/// <c>InvalidateAllPerViewOverridesForSheet</c> is the single documented choke point that drops
/// all nine, but it was previously only ever called with the *active* sheet's id (metadata-setter
/// forward-apply, Undo/Redo re-seeding) -- never for the sheet id a
/// <see cref="WorkbookSession.DeleteActiveSheet"/> call had just removed from the workbook. So
/// every deleted sheet permanently left a stale entry behind in each of those ten dictionaries for
/// the rest of the session's lifetime. Fixed by calling
/// <c>InvalidateAllPerViewOverridesForSheet(sheetId)</c> and
/// <c>_splitPaneViewportOffsets.Remove(sheetId)</c> for the just-deleted sheet id inside
/// <see cref="WorkbookSession.DeleteActiveSheet"/>.
/// </summary>
public sealed class R126_DeleteActiveSheetPurgesPerViewOverrideCachesTests
{
    /// <summary>
    /// Covers the five lazily-seeded-on-read overrides (zoom/mode/gridlines/headings/formulas)
    /// plus the split-pane viewport offsets -- seeded through the SAME real setters/getters the
    /// WPF/Avalonia View tab uses, on a <see cref="WorkbookSession.CreateSiblingView"/> (View ▸ New
    /// Window) whose own view diverges from the shared <see cref="Sheet"/> defaults, matching the
    /// R85/R87 per-window-independence tests already in this file's sibling suite.
    /// </summary>
    [Fact]
    public void DeleteActiveSheet_PurgesLazySeededOverrideCachesAndSplitPaneOffsetsForDeletedSheet()
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

        sibling.SetZoomPercent(150).Success.Should().BeTrue();
        sibling.SetWorksheetViewMode(WorksheetViewMode.PageLayout).Success.Should().BeTrue();
        sibling.SetShowGridlines(false).Success.Should().BeTrue();
        sibling.SetShowHeadings(false).Success.Should().BeTrue();
        sibling.SetShowFormulas(true).Success.Should().BeTrue();
        sibling.ExecuteReviewCommand(new SetSplitPanesCommand(details.Id, null, 5)).Success.Should().BeTrue();
        sibling.HasIndependentSplitPaneTopRight.Should().BeTrue();
        sibling.SetSplitPaneTopRightLeftCol(3).Should().BeTrue();

        // Each setter above re-seeds only its OWN entry after invalidating every per-view cache for
        // this sheet (InvalidateAllPerViewOverridesForSheet is a single choke point that clears all
        // nine unconditionally) -- so re-read the lazy-seeded properties now, exactly as the real
        // View tab status bar/ribbon does on every render, to reseed their entries with the values
        // just applied before checking anything.
        sibling.ZoomPercent.Should().Be(150);
        sibling.ViewMode.Should().Be(WorksheetViewMode.PageLayout);
        sibling.IsShowingGridlines.Should().BeFalse();
        sibling.IsShowingHeadings.Should().BeFalse();
        sibling.IsShowingFormulas.Should().BeTrue();

        var lazyFieldNames = new[]
        {
            "_viewZoomOverrides",
            "_viewModeOverrides",
            "_viewShowGridlinesOverrides",
            "_viewShowHeadingsOverrides",
            "_viewShowFormulasOverrides",
        };

        // Sanity: every cache really did get (re)seeded for Details before we delete it, otherwise
        // this test would trivially pass either way.
        foreach (var fieldName in lazyFieldNames)
            GetDictionaryKeys(sibling, fieldName).Should().Contain(details.Id, $"{fieldName} should be seeded before delete");
        GetDictionaryKeys(sibling, "_splitPaneViewportOffsets").Should().Contain(details.Id);

        var result = sibling.DeleteActiveSheet();

        result.Success.Should().BeTrue();
        workbook.Sheets.Should().NotContain(s => s.Id == details.Id);

        foreach (var fieldName in lazyFieldNames)
        {
            GetDictionaryKeys(sibling, fieldName).Should().NotContain(
                details.Id,
                $"{fieldName} must drop the deleted sheet's entry, not leak it for the rest of the session");
        }
        GetDictionaryKeys(sibling, "_splitPaneViewportOffsets").Should().NotContain(
            details.Id,
            "the split-pane viewport offsets cache must drop the deleted sheet's entry too");
    }

    /// <summary>
    /// Covers the four "pure peek, no seed-on-read" Freeze Panes/Split overrides
    /// (<c>_viewFrozenRowsOverrides</c>/<c>_viewFrozenColsOverrides</c>/<c>_viewSplitRowOverrides</c>/
    /// <c>_viewSplitColOverrides</c>), which -- unlike the five lazy ones above -- are only ever
    /// populated by the up-front <c>SeedViewSplitAndFrozenOverrides</c> snapshot a
    /// <see cref="WorkbookSession.CreateSiblingView"/> takes the moment it starts observing a sheet
    /// (see that method's remarks), so this deletes the sibling's initial sheet with no further
    /// mutating command in between.
    /// </summary>
    [Fact]
    public void DeleteActiveSheet_PurgesFrozenAndSplitRowColOverridesForDeletedSheet()
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

        var peekFieldNames = new[]
        {
            "_viewFrozenRowsOverrides",
            "_viewFrozenColsOverrides",
            "_viewSplitRowOverrides",
            "_viewSplitColOverrides",
        };

        foreach (var fieldName in peekFieldNames)
            GetDictionaryKeys(sibling, fieldName).Should().Contain(details.Id, $"{fieldName} should be seeded before delete");

        var result = sibling.DeleteActiveSheet();

        result.Success.Should().BeTrue();
        workbook.Sheets.Should().NotContain(s => s.Id == details.Id);

        foreach (var fieldName in peekFieldNames)
        {
            GetDictionaryKeys(sibling, fieldName).Should().NotContain(
                details.Id,
                $"{fieldName} must drop the deleted sheet's entry, not leak it for the rest of the session");
        }
    }

    /// <summary>
    /// No-regression sibling: deleting one sheet must not disturb another *surviving* sheet's own
    /// per-view override cache entries -- the purge must be scoped to the deleted sheet id only.
    /// </summary>
    [Fact]
    public void DeleteActiveSheet_LeavesSurvivingSheetsOwnOverrideCachesIntact()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        // Summary is already the active sheet at session creation -- seed its zoom/view-mode
        // directly, no SelectSheet round-trip needed.
        session.SetZoomPercent(175).Success.Should().BeTrue();
        session.SetWorksheetViewMode(WorksheetViewMode.PageBreakPreview).Success.Should().BeTrue();

        session.SelectSheet(details.Id).Should().BeTrue();
        session.SetZoomPercent(80).Success.Should().BeTrue();

        var result = session.DeleteActiveSheet();

        result.Success.Should().BeTrue();
        workbook.Sheets.Should().ContainSingle().Which.Id.Should().Be(summary.Id);

        // Reading these now goes through ApplySuccessfulWorkbookStructureResult's post-delete
        // active-sheet switch back to Summary; GetEffectiveViewState-equivalent reads must still
        // report Summary's OWN remembered 175/PageBreakPreview, not the workbook defaults and not
        // Details' now-purged 80.
        session.ZoomPercent.Should().Be(175);
        session.ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
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
