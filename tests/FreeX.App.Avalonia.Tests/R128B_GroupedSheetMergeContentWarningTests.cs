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

                // Group Sheet1 (active) with Sheet2 -- SelectAllVisibleSheets groups every visible
                // sheet WITHOUT changing the active sheet, matching Excel's Ctrl/Shift-click sheet-tab
                // grouping gesture.
                window.Session.SelectAllVisibleSheets();
                window.Session.IsWorkbookGrouped.Should().BeTrue();
                window.Session.ActiveSheet.Id.Should().Be(sheet1.Id);

                // The range on the ACTIVE sheet (Sheet1) is empty -- pre-fix, analyzing only Sheet1
                // finds nothing to lose.
                var range = new GridRange(
                    new CellAddress(sheet1.Id, 1, 2),
                    new CellAddress(sheet1.Id, 1, 3)); // B1:C1

                // Sheet2's C1 -- the same range's non-top-left cell once remapped -- holds real
                // content that the grouped-sheet fan-out merge is about to blank.
                window.Session.SelectSheet(sheet2.Id);
                window.Session.BeginFormulaEdit(new CellAddress(sheet2.Id, 1, 3));
                window.Session.CommitCellText("keep-me").Success.Should().BeTrue();
                window.Session.SelectSheet(sheet1.Id);
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
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
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

                await InvokePrivateAsync(window, "MergeAndCenterSelectedRangeAsync");

                window.OwnedWindows.Should().BeEmpty("neither grouped sheet has any content to lose");
                sheet1.MergedRegions.Should().Contain(range);
                sheet2.MergedRegions.Should().Contain(new GridRange(
                    new CellAddress(sheet2.Id, 1, 2),
                    new CellAddress(sheet2.Id, 1, 3)));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
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
                window.Session.SelectSheet(sheet1.Id);
                window.Session.SelectAllVisibleSheets();
                window.Session.IsWorkbookGrouped.Should().BeTrue();
                window.Session.ActiveSheet.Id.Should().Be(sheet1.Id);

                var range = new GridRange(
                    new CellAddress(sheet1.Id, 1, 2),
                    new CellAddress(sheet1.Id, 1, 3)); // B1:C1 on Sheet1, empty.

                window.Session.SelectSheet(sheet2.Id);
                window.Session.BeginFormulaEdit(new CellAddress(sheet2.Id, 1, 3));
                window.Session.CommitCellText("keep-me-too").Success.Should().BeTrue();
                window.Session.SelectSheet(sheet1.Id);
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
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
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
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
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
}
