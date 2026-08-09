using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// SCOPE CLOSURE (r128b): the r128 fix for the grouped-sheet Merge &amp; Center content-loss warning
/// landed only in the WPF host (src/FreeX.App.Host/MainWindow.HomeFormatting.cs,
/// R128-homeformatting-groupedsheet-merge-1 / R128_GroupedSheetMergeContentWarningTests). The Avalonia
/// shell is a fully separate implementation and independently supports sheet-tab grouping
/// (<c>_session.IsWorkbookGrouped</c>, <c>WorkbookSession.CurrentGroupedEditSheetIds()</c>), but its two
/// merge content-loss warnings still analyzed only the ACTIVE sheet:
///
/// - <c>MergeAndCenterSelectedRangeAsync</c> (src/FreeX.App.Avalonia/MainWindow.cs) called
///   <c>CellMergePlanner.AnalyzeContent(_session.ActiveSheet, areas)</c>, while the execution it gates
///   (<c>_session.MergeAndCenterSelectedRange</c> -&gt; <c>WorkbookSession.CreateMergeAndCenterCommand</c>)
///   fans the same ranges out across every <c>CurrentGroupedEditSheetIds()</c> sheet, blanking
///   non-top-left cells there unconditionally.
/// - <c>ShowFormatCellsDialogAsync</c>'s Merge Cells checkbox called
///   <c>CellMergePlanner.AnalyzeContent(_session.ActiveSheet, ...)</c>, while the execution it gates
///   (<c>_session.ApplySelectedRangeCompactFormat</c> -&gt;
///   <c>WorkbookSession.CreateFormatCellsMergeCommands</c>) fans out the same way.
///
/// Both left a non-active GROUPED sheet's content silently discarded with zero warning. The fix adds a
/// shared <c>AnalyzeGroupedSheetMergeContent</c> choke point (mirroring the WPF host's
/// <c>TryResolveMergeContentResolution</c>) that remaps the ranges onto every grouped sheet via
/// <c>GroupedSheetRangePlanner.RemapRangeToSheet</c> and unions their content-loss entries via
/// <c>CellMergePlanner.AnalyzeContent(IEnumerable&lt;(Sheet,Ranges)&gt;, bool)</c> -- the same overload the
/// WPF choke point uses.
///
/// These tests drive the REAL production entry points via reflection (both are private), with a real
/// grouped-sheet selection (<c>WorkbookSession.SelectAllVisibleSheets</c>) and a real modal warning
/// dialog, mirroring R128_MergeAndCenterMultiAreaToggleContentLossTests' and
/// R128B_FormatCellsMultiAreaMergeContentWarningTests' established patterns for controlling real async
/// dialog suspension points headlessly.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R128B_GroupedSheetMergeContentWarningTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task MergeAndCenter_GroupedSheets_NonActiveSheetContent_TriggersWarningAndCancelPreservesData()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                var sheet1 = window.Session.Workbook.Sheets[0];
                var sheet2 = window.Session.Workbook.AddSheet("GroupedSiblingLoss");
                window.Session.SelectSheet(sheet1.Id);

                var range = new GridRange(
                    new CellAddress(sheet1.Id, 1, 2),
                    new CellAddress(sheet1.Id, 1, 3)); // B1:C1

                // Make this test's premise -- "the ACTIVE sheet's range is empty" -- actually TRUE.
                // `new MainWindow([])` does NOT open a blank workbook: with no startup file args,
                // StartupWorkbookLoader falls back to PortPreviewWorkbookFactory's sample workbook
                // (StartupWorkbookLoader.cs:39-41), which seeds B1="Windows"/C1="macOS"
                // (PortPreviewWorkbookFactory.cs:50-51) -- exactly the B1:C1 this test selects. Left
                // seeded, the pre-fix code would have found content loss on Sheet1 ALONE and raised the
                // warning for the wrong reason, so this test would have passed with the grouped-sheet
                // fix reverted -- certifying a bug rather than catching it.
                //
                // ALL per-sheet setup happens BEFORE grouping. SelectSheet(id) is a plain tab click
                // (toggle: false), and it routes through UpdateGroupedSheetsForTabSelection, which
                // UNGROUPS -- exactly as clicking a single sheet tab does in Excel. Doing the sibling
                // edit after SelectAllVisibleSheets would therefore silently dissolve the grouping and
                // leave this test merging a plain single-sheet selection, testing nothing it claims to.
                //
                // Sheet2's C1 -- the same range's non-top-left cell once remapped -- holds real content
                // that the grouped-sheet fan-out merge is about to blank.
                window.Session.SelectSheet(sheet2.Id);
                window.Session.BeginFormulaEdit(new CellAddress(sheet2.Id, 1, 3));
                window.Session.CommitCellText("keep-me").Success.Should().BeTrue();

                // Clear WHILE STILL UNGROUPED: once grouped, ClearSelectedRangeContents fans out across
                // every grouped sheet and would wipe the sibling "keep-me" content just written.
                window.Session.SelectSheet(sheet1.Id);
                window.Session.SelectRange(range);
                window.Session.ClearSelectedRangeContents().Success.Should().BeTrue();

                // Group Sheet1 (active) with Sheet2 -- SelectAllVisibleSheets groups every visible
                // sheet WITHOUT changing the active sheet, matching Excel's Ctrl/Shift-click sheet-tab
                // grouping gesture. Nothing below may call SelectSheet, or the grouping dissolves.
                window.Session.SelectAllVisibleSheets();
                window.Session.IsWorkbookGrouped.Should().BeTrue();
                window.Session.ActiveSheet.Id.Should().Be(sheet1.Id);

                // The ACTIVE sheet's range is now genuinely empty while the grouped sibling holds
                // content -- pre-fix, analyzing only Sheet1 finds nothing to lose, which is precisely
                // the defect this test exists to catch.
                window.Session.SelectRange(range);

                var task = InvokePrivateTaskAsync(window, "MergeAndCenterSelectedRangeAsync");
                await DrainInputAsync();

                // THE DEFECT: because only the active sheet (Sheet1, empty) was analyzed, the pre-fix
                // code never showed the warning here, and the fan-out merge that followed would have
                // silently discarded Sheet2's "keep-me" content.
                window.OwnedWindows.Should().ContainSingle(
                    "a grouped sheet's content is about to be blanked by the fan-out merge, even " +
                    "though the active sheet's own range is empty");
                var dialog = window.OwnedWindows.OfType<Window>().Single();
                AutomationProperties.GetAutomationId(dialog).Should().Be("MergeCellsContentWarningDialog");

                dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await DrainInputAsync();
                await task;

                sheet1.MergedRegions.Should().BeEmpty(
                    "cancelling the warning must abort the whole grouped-sheet merge");
                sheet2.MergedRegions.Should().BeEmpty(
                    "cancelling the warning must abort the whole grouped-sheet merge");
                sheet2.GetCell(new CellAddress(sheet2.Id, 1, 3))!.Value.Should().Be(new TextValue("keep-me"),
                    "Sheet2's content must survive an aborted grouped-sheet merge -- this is exactly " +
                    "the content the pre-fix bug silently discarded with no warning at all");
            }
            finally
            {
                CloseWindowAndOwnedDialogs(window);
            }

            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// No-regression sibling: when NO sheet in the group (active or otherwise) has any content in the
    /// merge range, the widened grouped-sheet analysis must not manufacture a false-positive warning --
    /// the merge must proceed silently across every grouped sheet, exactly as intended.
    /// </summary>
    [Fact]
    public async Task MergeAndCenter_GroupedSheets_NoContentAnywhere_MergesSilentlyAcrossBothSheets()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                var sheet1 = window.Session.Workbook.Sheets[0];
                var sheet2 = window.Session.Workbook.AddSheet("GroupedNoContent");
                window.Session.SelectSheet(sheet1.Id);
                window.Session.SelectAllVisibleSheets();
                window.Session.IsWorkbookGrouped.Should().BeTrue();

                var range = new GridRange(
                    new CellAddress(sheet1.Id, 1, 2),
                    new CellAddress(sheet1.Id, 1, 3)); // B1:C1
                window.Session.SelectRange(range);

                // R129B-avalonia-groupedsheetmerge-nocontent-1: `new MainWindow([])` does NOT start
                // from a blank sheet -- with no startup file args, StartupWorkbookLoader falls back to
                // PortPreviewWorkbookFactory's sample/demo workbook (StartupWorkbookLoader.cs:39-41,
                // "Showing sample workbook."), which seeds Sheet1's row 1 with real header text
                // including B1="Windows"/C1="macOS" (PortPreviewWorkbookFactory.cs:50-51) -- exactly
                // the B1:C1 range this test selects. Pre-fix, that made this "no content anywhere"
                // scenario silently FALSE: the grouped-sheet content analysis correctly found real
                // content on sheet1 and opened the real warning dialog, which nothing in this test
                // path was written to handle -- so `await mergeTask` blocked forever on the dialog's
                // never-resolving ShowDialog task, hanging this test (and, once discovered under any
                // other test ahead of it in the same process, the whole assembly). This is a genuine
                // product behavior (the sample-workbook fallback), not a bug, so the fix is on the test
                // side: explicitly clear the range's content on every grouped sheet before merging, so
                // the scenario this test's name promises -- content-free cells on every grouped sheet --
                // is actually true regardless of what the sample workbook happens to seed. Clearing
                // through the session (rather than hand-editing the Sheet/Cell model) exercises the
                // same fan-out CurrentGroupedEditSheetIds() plumbing as the merge itself, so this stays
                // a real headless-UI test rather than reaching around the production code path.
                window.Session.ClearSelectedRangeContents().Success.Should().BeTrue();

                // Do NOT `await` the method directly from inside Session.Dispatch: we are already ON
                // the UI thread, so anything MergeAndCenterSelectedRangeAsync posts back to the
                // dispatcher can never run while we block on its Task -- a self-deadlock that hangs
                // this test (and therefore the whole assembly, and therefore the gate) forever
                // rather than failing. The two sibling tests in this file already use the correct
                // shape: start the task, PUMP the dispatcher, then await. Match them even though no
                // dialog is expected here -- the pumping is what lets the method finish at all.
                var mergeTask = InvokePrivateTaskAsync(window, "MergeAndCenterSelectedRangeAsync");
                await DrainInputAsync();
                await mergeTask;

                window.OwnedWindows.Should().BeEmpty("neither grouped sheet has any content to lose");
                sheet1.MergedRegions.Should().Contain(range);
                sheet2.MergedRegions.Should().Contain(new GridRange(
                    new CellAddress(sheet2.Id, 1, 2),
                    new CellAddress(sheet2.Id, 1, 3)));
            }
            finally
            {
                CloseWindowAndOwnedDialogs(window);
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormatCellsMerge_GroupedSheets_NonActiveSheetContent_TriggersWarningAndCancelPreservesData()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new global::Avalonia.Size(1120, 720));
                window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var sheet1 = window.Session.Workbook.Sheets[0];
                var sheet2 = window.Session.Workbook.AddSheet("FormatCellsGroupedSiblingLoss");
                var range = new GridRange(
                    new CellAddress(sheet1.Id, 1, 2),
                    new CellAddress(sheet1.Id, 1, 3)); // B1:C1 on Sheet1.

                // Same ordering rule as the Merge & Center test above: every per-sheet edit happens
                // BEFORE grouping, because SelectSheet is a plain tab click that ungroups. And Sheet1's
                // B1:C1 must be cleared explicitly -- `new MainWindow([])` opens the sample workbook,
                // which seeds B1="Windows"/C1="macOS" there, so without the clear the pre-fix code would
                // raise the warning off the ACTIVE sheet's own content and this test would pass with the
                // grouped-sheet fix reverted.
                window.Session.SelectSheet(sheet2.Id);
                window.Session.BeginFormulaEdit(new CellAddress(sheet2.Id, 1, 3));
                window.Session.CommitCellText("keep-me-too").Success.Should().BeTrue();

                window.Session.SelectSheet(sheet1.Id);
                window.Session.SelectRange(range);
                window.Session.ClearSelectedRangeContents().Success.Should().BeTrue();

                window.Session.SelectAllVisibleSheets();
                window.Session.IsWorkbookGrouped.Should().BeTrue();
                window.Session.ActiveSheet.Id.Should().Be(sheet1.Id);
                window.Session.SelectRange(range);

                // The "Merge cells" checkbox lives on the Alignment tab (index 1).
                var formatCellsTask = InvokePrivateTaskAsync(window, "ShowFormatCellsDialogAsync", 1);
                await DrainInputAsync();

                var formatCellsDialog = FindOwnedWindow(window, "FormatCellsCompactDialog");
                formatCellsDialog.UpdateLayout();
                var mergeCellsBox = formatCellsDialog.GetVisualDescendants()
                    .OfType<CheckBox>()
                    .FirstOrDefault(candidate =>
                        AutomationProperties.GetAutomationId(candidate) == "FormatCellsMergeCellsBox");
                mergeCellsBox.Should().NotBeNull("the Merge cells checkbox must be present in the dialog");
                mergeCellsBox!.IsChecked = true;

                var okButton = formatCellsDialog.GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(candidate =>
                        AutomationProperties.GetAutomationId(candidate) == "FormatCellsOkButton");
                okButton.Should().NotBeNull("the OK button must be present in the dialog");
                okButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();

                // THE DEFECT: because only the active sheet (Sheet1, empty) was analyzed, the pre-fix
                // code never showed the warning here, and the grouped-sheet fan-out merge that followed
                // (WorkbookSession.CreateFormatCellsMergeCommands) would have silently discarded
                // Sheet2's "keep-me-too" content.
                window.OwnedWindows.Should().ContainSingle(
                    "a grouped sheet's content is about to be blanked by the Format Cells fan-out " +
                    "merge, even though the active sheet's own range is empty");
                var warningDialog = window.OwnedWindows.OfType<Window>().Single();
                AutomationProperties.GetAutomationId(warningDialog).Should().Be("MergeCellsContentWarningDialog");

                warningDialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await DrainInputAsync();
                await formatCellsTask;

                sheet1.MergedRegions.Should().BeEmpty(
                    "cancelling the warning must abort the whole Format Cells apply");
                sheet2.MergedRegions.Should().BeEmpty(
                    "cancelling the warning must abort the whole Format Cells apply");
                sheet2.GetCell(new CellAddress(sheet2.Id, 1, 3))!.Value.Should().Be(new TextValue("keep-me-too"),
                    "Sheet2's content must survive an aborted Format Cells apply -- this is exactly " +
                    "the content the pre-fix bug silently discarded with no warning at all");
            }
            finally
            {
                CloseWindowAndOwnedDialogs(window);
            }

            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// No-regression sibling: the ordinary UNGROUPED case (the vast majority of Format Cells merges)
    /// must be unaffected by routing the content-loss analysis through the grouped-sheet choke point --
    /// GetCurrentGroupedEditSheetIds() returns just the active sheet when nothing is grouped, so the
    /// active sheet's own content-loss warning must still fire exactly as before.
    /// </summary>
    [Fact]
    public async Task FormatCellsMerge_Ungrouped_ActiveSheetContent_StillTriggersWarning()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new global::Avalonia.Size(1120, 720));
                window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var sheet = window.Session.Workbook.AddSheet("FormatCellsUngrouped");
                window.Session.SelectSheet(sheet.Id);
                window.Session.IsWorkbookGrouped.Should().BeFalse();

                var range = new GridRange(
                    new CellAddress(sheet.Id, 1, 2),
                    new CellAddress(sheet.Id, 1, 3)); // B1:C1
                window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 1, 3));
                window.Session.CommitCellText("active-sheet-data").Success.Should().BeTrue();
                window.Session.SelectRange(range);

                var formatCellsTask = InvokePrivateTaskAsync(window, "ShowFormatCellsDialogAsync", 1);
                await DrainInputAsync();

                var formatCellsDialog = FindOwnedWindow(window, "FormatCellsCompactDialog");
                formatCellsDialog.UpdateLayout();
                var mergeCellsBox = formatCellsDialog.GetVisualDescendants()
                    .OfType<CheckBox>()
                    .First(candidate =>
                        AutomationProperties.GetAutomationId(candidate) == "FormatCellsMergeCellsBox");
                mergeCellsBox.IsChecked = true;

                var okButton = formatCellsDialog.GetVisualDescendants()
                    .OfType<Button>()
                    .First(candidate =>
                        AutomationProperties.GetAutomationId(candidate) == "FormatCellsOkButton");
                okButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();

                window.OwnedWindows.Should().ContainSingle("the active sheet itself still has content that would be lost");
                var warningDialog = window.OwnedWindows.OfType<Window>().Single();
                var keepFirstButton = warningDialog.GetVisualDescendants()
                    .OfType<Button>()
                    .First(candidate =>
                        AutomationProperties.GetAutomationId(candidate) == "MergeCellsKeepFirstButton");
                keepFirstButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                await formatCellsTask;

                sheet.MergedRegions.Should().Contain(range);
            }
            finally
            {
                CloseWindowAndOwnedDialogs(window);
            }

            return true;
        }, CancellationToken.None);
    }

    private static Window FindOwnedWindow(MainWindow window, string automationId)
    {
        var match = window.OwnedWindows
            .OfType<Window>()
            .FirstOrDefault(candidate => AutomationProperties.GetAutomationId(candidate) == automationId);
        match.Should().NotBeNull($"a window with automation id '{automationId}' must be owned by MainWindow");
        return match!;
    }

    private static Task InvokePrivateAsync(MainWindow window, string methodName, params object[] args)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new System.MissingMethodException(nameof(MainWindow), methodName);
        return (Task)method.Invoke(window, args)!;
    }

    private static Task InvokePrivateTaskAsync(MainWindow window, string methodName, params object[] args) =>
        InvokePrivateAsync(window, methodName, args);

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    /// <summary>
    /// R128B: close OWNED DIALOGS before the owner. These tests deliberately open real modal warning
    /// dialogs; closing only the owner does NOT reliably dispose them in Avalonia headless, so a
    /// still-open owned Window survives into the next test in the same process and the whole assembly
    /// hangs -- which is far worse than a failure, because a hang reports nothing at all. This class
    /// passed when run ALONE and hung when paired with any other class, which is the signature of
    /// leaked per-test state rather than a bad assertion. Cleanup must also survive an assertion
    /// throwing mid-test, which is exactly when a dialog is most likely to be left open.
    /// </summary>
    private static void CloseWindowAndOwnedDialogs(Window window)
    {
        foreach (var owned in window.OwnedWindows.OfType<Window>().ToArray())
        {
            try { owned.Close(); } catch { /* best effort: never mask the real test failure */ }
        }

        try
        {
            (window as MainWindow)?.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
        catch { /* best effort */ }
    }
}
