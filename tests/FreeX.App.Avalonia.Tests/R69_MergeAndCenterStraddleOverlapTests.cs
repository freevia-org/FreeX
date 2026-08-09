using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R69-commands-merge-center-6-1 (src/FreeX.App.Avalonia/MainWindow.cs,
/// MergeAndCenterSelectedRangeAsync). The Avalonia Merge &amp; Center handler used to gate its
/// unmerge-toggle on <c>WorkbookSession.IsSelectedRangeMerged</c> (= <c>CellMergePlanner.
/// IsSelectionMerged</c>, true for ANY overlap with an existing merged region). A selection that only
/// PARTIALLY overlaps an existing merge -- e.g. merging B1:D1 first, then selecting the straddling
/// A1:C1 -- wrongly matched that "any overlap" check and got silently UNMERGED instead of being
/// rejected with the "Range overlaps an existing merged region." conflict error Excel/the WPF host
/// give for a genuine straddling overlap.
///
/// The fix routes the toggle decision through <c>CellMergePlanner.FindCoveringRegion</c> (the same
/// full-containment test <c>CreateMergeAndCenterCommands</c> already used internally, and that the WPF
/// host drives through <c>MainWindow.HomeFormatting.cs</c>'s <c>CreateMergeAndCenterCommand</c>): only a
/// selection FULLY CONTAINED in one existing merged region toggles to unmerge; a partial/straddling
/// overlap falls through to the normal merge path, which <c>MergeCellsCommand</c> correctly rejects.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R69_MergeAndCenterStraddleOverlapTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task MergeAndCenter_StraddlingOverlap_IsRejected_NotUnmerged()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("StraddleFixture");
            window.Session.SelectSheet(sheet.Id);

            // Merge B1:D1 first.
            var existingMerge = new GridRange(
                new CellAddress(sheet.Id, 1, 2),
                new CellAddress(sheet.Id, 1, 4));
            window.Session.SelectRange(existingMerge);
            await InvokePrivateAsync(window, "MergeAndCenterSelectedRangeAsync");
            sheet.MergedRegions.Should().Contain(existingMerge, "the setup merge must succeed");

            // Select the straddling A1:C1 (overlaps B1:D1's left half only -- neither range contains
            // the other) and click Merge & Center again.
            var straddlingRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 3));
            window.Session.SelectRange(straddlingRange);
            await InvokePrivateAsync(window, "MergeAndCenterSelectedRangeAsync");

            sheet.MergedRegions.Should().Contain(existingMerge,
                "a straddling/partial overlap must be REJECTED, not treated as the unmerge-toggle gesture -- " +
                "B1:D1 must remain merged");
            window.StatusTextForTest.Text.Should().Contain("overlaps",
                "the straddling overlap must surface the same conflict error MergeCellsCommand raises " +
                "for a genuine overlapping-merge request");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MergeAndCenter_SelectionIsExactlyTheExistingMerge_StillTogglesToUnmerge()
    {
        // Sibling/no-regression case: the full-containment toggle gesture (selection IS the existing
        // merged region) must keep working exactly as it did before this fix.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ExactToggleFixture");
            window.Session.SelectSheet(sheet.Id);

            var range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2));
            window.Session.SelectRange(range);
            await InvokePrivateAsync(window, "MergeAndCenterSelectedRangeAsync");
            sheet.MergedRegions.Should().Contain(range);

            await InvokePrivateAsync(window, "MergeAndCenterSelectedRangeAsync");

            sheet.MergedRegions.Should().NotContain(range,
                "selecting exactly the existing merged region and clicking Merge & Center again must still unmerge it");
            window.StatusTextForTest.Text.Should().Contain("Unmerged");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MergeAndCenter_CleanUnmergedSelection_StillMergesNormally()
    {
        // Sibling/no-regression case: a plain, never-merged selection must still merge on the first click.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanMergeFixture");
            window.Session.SelectSheet(sheet.Id);

            var range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 2));
            window.Session.SelectRange(range);

            window.Session.IsSelectedRangeMerged.Should().BeFalse();
            await InvokePrivateAsync(window, "MergeAndCenterSelectedRangeAsync");

            sheet.MergedRegions.Should().Contain(range);
            window.StatusTextForTest.Text.Should().Contain("Merged and centered");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static async Task InvokePrivateAsync(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new System.MissingMethodException(nameof(MainWindow), methodName);
        var task = (Task)method.Invoke(window, null)!;
        await task;
    }
}
