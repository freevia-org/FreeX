using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R128-avalonia-mainwindow-multiarea-3 (HIGH, data-loss): <c>MergeAndCenterSelectedRangeAsync</c>
/// (src/FreeX.App.Avalonia/MainWindow.cs) gated its whole-selection content-loss analysis
/// (<c>CellMergePlanner.AnalyzeContent(_session.ActiveSheet, areas)</c>, the r127 fix that made the
/// analysis cover every disjoint Ctrl+click area) behind <c>if (!isUnmergeToggle)</c>, where
/// <c>isUnmergeToggle</c> was computed from <c>CellMergePlanner.FindCoveringRegion</c> over the single
/// ACTIVE range only. So whenever the active (last-clicked) area happened to already be fully covered
/// by an existing merge -- the ordinary "click Merge &amp; Center again to unmerge" toggle gesture --
/// the analysis (and therefore the "merging cells can discard cell contents" warning) was skipped for
/// the WHOLE operation, even though <c>WorkbookSession.MergeAndCenterSelectedRange</c> still went on to
/// merge every OTHER selected area too, discarding any non-top-left content there with zero warning.
///
/// The fix calls <c>CellMergePlanner.AnalyzeContent(_session.ActiveSheet, areas)</c> unconditionally
/// (matching the WPF host's <c>TryResolveMergeContentResolution</c>, which has no such short-circuit at
/// all) and relies on <c>AnalyzeContent</c>'s own per-range logic to naturally report
/// <c>WouldLoseContent = false</c> for an area that is itself an existing full-region merge.
///
/// These tests drive the REAL production entry point (<c>MergeAndCenterSelectedRangeAsync</c>, invoked
/// via reflection since it is private) with a genuine multi-area Ctrl+click selection
/// (<c>WorkbookSession.SelectRanges</c>) and a real modal <c>Window</c> warning dialog
/// (<c>ShowMergeCellsContentWarningDialogAsync</c>'s <c>await dialog.ShowDialog(this)</c>), driven via a
/// synthetic Escape key press -- mirrors R115_StartupRecoveryDedupTests' established pattern for
/// controlling a real async dialog suspension point headlessly.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R128_MergeAndCenterMultiAreaToggleContentLossTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task MergeAndCenter_ActiveAreaAlreadyMerged_SiblingAreaHasLossyContent_StillWarnsBeforeDiscarding()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var sheet = window.Session.Workbook.AddSheet("ToggleSiblingLoss");
                window.Session.SelectSheet(sheet.Id);

                // Merge the ACTIVE area first (A1:B2), via the real handler -- it is empty, so this
                // merge itself never triggers the content-loss dialog.
                var activeArea = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 2, 2));
                window.Session.SelectRange(activeArea);
                await window.MergeAndCenterSelectedRangeForTestAsync();
                sheet.MergedRegions.Should().Contain(activeArea, "the setup merge of the active area must succeed");

                // A disjoint SIBLING area (D1:E1) that is NOT merged and holds content in its
                // non-top-left cell (E1) -- exactly what a real Merge & Center would discard.
                var siblingArea = new GridRange(
                    new CellAddress(sheet.Id, 1, 4),
                    new CellAddress(sheet.Id, 1, 5));
                window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 4));
                window.Session.CommitCellText("Keep").Success.Should().BeTrue();
                window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 5));
                window.Session.CommitCellText("Lost").Success.Should().BeTrue();

                // Ctrl+click siblingArea then activeArea (disjoint multi-area selection): the ACTIVE
                // range is activeArea (already merged -- the unmerge-toggle gesture), SelectedRanges
                // holds both areas, exactly what a real multi-area Ctrl+click leaves behind.
                window.Session.SelectRanges(activeArea, [siblingArea, activeArea]);

                var task = window.MergeAndCenterSelectedRangeForTestAsync();
                await DrainInputAsync();

                // THE DEFECT: because the active area is already merged (isUnmergeToggle == true), the
                // pre-fix code skipped AnalyzeContent for the WHOLE operation, so no dialog ever
                // appeared and the sibling area's "Lost" content was silently discarded by the merge
                // that followed.
                window.OwnedWindows.Should().ContainSingle(
                    "the sibling area's discardable content must still be flagged even though the " +
                    "ACTIVE area is only being toggled to unmerge -- the analysis must cover every " +
                    "disjoint area, not skip entirely because the active one is a toggle");
                var dialog = window.OwnedWindows.Single();
                global::Avalonia.Automation.AutomationProperties.GetAutomationId(dialog)
                    .Should().Be("MergeCellsContentWarningDialog");

                // Cancel the warning: the WHOLE operation (including the active area's unmerge-toggle)
                // must abort, exactly like a genuine single-area content-loss cancel does.
                dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await DrainInputAsync();
                await task;

                sheet.MergedRegions.Should().Contain(activeArea,
                    "cancelling the warning must abort the whole operation -- the active area's " +
                    "unmerge-toggle must not have happened");
                sheet.MergedRegions.Should().NotContain(siblingArea,
                    "cancelling the warning must abort the whole operation -- the sibling area must " +
                    "not have been merged");
                sheet.GetCell(new CellAddress(sheet.Id, 1, 4))!.Value.Should().Be(new TextValue("Keep"));
                sheet.GetCell(new CellAddress(sheet.Id, 1, 5))!.Value.Should().Be(new TextValue("Lost"),
                    "the sibling area's non-top-left content must survive an aborted merge -- this is " +
                    "exactly the content the pre-fix bug silently discarded with no warning at all");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Sibling/no-regression case: when the sibling area has NO discardable content (only its own
    /// top-left cell is non-empty), the unconditional <c>AnalyzeContent</c> call must NOT pop a
    /// spurious warning dialog -- the toggle and the sibling merge must both proceed exactly as they
    /// did before this fix.
    /// </summary>
    [Fact]
    public async Task MergeAndCenter_ActiveAreaAlreadyMerged_SiblingAreaHasOnlyTopLeftContent_MergesWithoutWarning()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var sheet = window.Session.Workbook.AddSheet("ToggleSiblingClean");
                window.Session.SelectSheet(sheet.Id);

                var activeArea = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 2, 2));
                window.Session.SelectRange(activeArea);
                await window.MergeAndCenterSelectedRangeForTestAsync();
                sheet.MergedRegions.Should().Contain(activeArea, "the setup merge of the active area must succeed");

                var siblingArea = new GridRange(
                    new CellAddress(sheet.Id, 1, 4),
                    new CellAddress(sheet.Id, 1, 5));
                window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 4));
                window.Session.CommitCellText("OnlyTopLeft").Success.Should().BeTrue();
                // E1 (siblingArea's non-top-left cell) is deliberately left blank.

                window.Session.SelectRanges(activeArea, [siblingArea, activeArea]);

                await window.MergeAndCenterSelectedRangeForTestAsync();

                window.OwnedWindows.Should().BeEmpty(
                    "a sibling area whose only content is already in its own top-left cell must not " +
                    "trigger the content-loss warning");
                sheet.MergedRegions.Should().NotContain(activeArea,
                    "the active area's unmerge-toggle must still fire when nothing would be lost");
                sheet.MergedRegions.Should().Contain(siblingArea,
                    "the sibling area must still be merged alongside the active area's toggle");
                sheet.GetCell(new CellAddress(sheet.Id, 1, 4))!.Value.Should().Be(new TextValue("OnlyTopLeft"));
                window.StatusTextForTest.Text.Should().Contain("Unmerged");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
